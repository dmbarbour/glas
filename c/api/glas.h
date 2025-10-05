/**
 * Use glas runtime as a library.
 * 
 * Clients manipulate data stacks and registers, import glas namespaces
 * or define names through callbacks, then invoke programs defined in 
 * scope. Information transfer is pass-by-copy, integers and binaries.
 * 
 * Clients may interact with transactions, though preserving atomicity
 * and isolation can be difficult. It's at least feasible to set up some
 * callbacks for precommit/commit/abort/etc..
 * 
 * Eventually, this runtime should support binding a supported database
 * for persistent, shared registers. Also, imports from DVCS repositories.
 * But those are long-term goals.
 * 
 */
#pragma once
#ifndef GLAS_H
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

/*******************************************
 * RUNTIME CONTEXT
 ******************************************/

/**
 * Reference to an opaque glas context.
 * 
 * Each glas context logically consists of:
 * - a coroutine, controlled via this API
 * - a data stack and auxilliary stash
 * - a lexically scoped view of a namespace
 * 
 * Through this API, a client issues commands for that coroutine.
 * All commands are synchronous, i.e. caller waits for a result, 
 * and the coroutine 'runs' in the calling thread.
 */
typedef struct glas glas;

/**
 * Create a new glas context.
 * 
 * This starts with an empty namespace, but users may load definitions,
 * registers, and callbacks. 
 * 
 * The data stack and stash are logically infinite, filled with zeroes.
 * Thus, there is no risk of stack underflow. But clients should try
 * to respect static stack arity constraints.
 */
glas* glas_create();

/**
 * Fork a context. The returned context shares the namespace but
 * starts with an empty data stack. Consider use of data_xchg to
 * move some data to the fork.
 * 
 * The glas model has fork-join semantics for coroutines. A fork
 * within a transaction must terminate before that transaction 
 * may commit. A fork from a callback context must terminate for
 * the caller to proceed. Use glas_destroy to terminate the fork.
 * 
 * However, outside of a transaction or callback, there is nothing
 * to join, nothing waiting on destroy. Thus, contextually, forks
 * may be used as threads.
 */
glas* glas_create_fork(glas*);

/**
 * Destroy a glas context.
 * 
 * This is for contexts obtained via glas_create or glas_create_fork.
 * Those obtained via callbacks are implicitly destroyed on return.
 * Basically: if you create it, you destroy it.
 */
void glas_destroy(glas*);



/**
 * Query data stack arity of computation so far.
 * 
 * - in: number of zeroes read from stack or stash; should be zero
 * - out: number of items on stack or stash relative to input
 * - max: the peak value for 'out', relative to input
 * 
 */
void glas_refl_stack_arity(glas*, size_t* in, size_t* max, size_t* out);


/**
 * Control data linearity checks.
 * 
 * Several operations - move, copy, drop, register get and set, etc. -
 * will fail if they would copy or drop linear data. This is a good
 * default, but the client is free to disable these checks.
 * 
 * Note this also applies to programs run within the context, and is
 * inherited by forks or callback contexts like the lexical namespace.
 */
void glas_cfg_set_linearity_checks(glas*, bool enable);
bool glas_cfg_get_linearity_checks(glas*);

/**
 * Control name shadowing checks.
 * 
 * Shadowing names by accident can be painful, so it's suggested that
 * the client make the intention explicit. These checks apply when the
 * client introduces a name or prefix.
 */
void glas_cfg_set_shadowing_checks(glas*, bool enable);
bool glas_cfg_get_shadowing_checks(glas*);


/*******************************************
 *  DATA STACK MANIPULATION
 *******************************************/

/**
 * Visualize data shuffling based on a simple moves string.
 *   
 *   "abc-abcabc"   copy 3
 *   "abc-b"        drops a and c
 *   "abcd-abcab"   drops d, copies ab to top of stack
 * 
 * This operation will read stack items into temporary variables based
 * on the LHS of '-'. Each character in [a-zA-Z] may be assigned at most 
 * once on LHS, then written any number of times on RHS. The rightmost
 * character represents 'top' of data stack.
 * 
 * This operation replaces the whole gamut of stack shuffling, but copy
 * and drop have dedicated operations for performance.
 * 
 * This operation fails, returning false, if it would copy or drop linear
 * data unless the context has been flagged to ignore linearity.
 * 
 * Note: If you're using this operation frequently, you should consider
 * introducing registers and simplifying your stack! Stacks are convenient
 * to avoid naming every little thing, but they grow unwieldy.
 */
bool glas_data_move(glas*, char const* moves);

/**
 * Specialized 'move' for copy and drop. User specifies the number 
 * of items. Like move, these operations may fail upon linear data
 * unless forced.
 */
bool glas_data_copy(glas*, size_t amt);
bool glas_data_drop(glas*, size_t amt);

/**
 * Move data to or from an auxilliary stack, called the stash.
 * 
 * If amt > 0, moves data to the stash. If amt < 0, transfers from stash
 * back to the data stack. Modulo efficiency, equivalent to moving one
 * element at a time.
 * 
 * The stash serves a similar role as 'dip' within the program model. 
 * It is not visible to program definitions, only to this API client. 
 */
void glas_data_stash(glas*, int amt);


/**
 * Stack exchange between two coroutines.
 * 
 * Move amt data elements from src to dst, preserving stack order.
 * (Or reverse direction if amt is negative.)
 * 
 * This operation will fail, returning false, if the two contexts
 * are in different transactional scopes. Transfer of 0 can test
 * for this condition.
 * 
 * This operation assumes both contexts are idle and controlled 
 * by the calling thread. Transfer between data stacks while in
 * concurrent use is very likely to break things. (Favor registers
 * for transfer between concurrent coroutines!)
 * 
 * Aside: This operation isn't possible in the glas program model
 * because coroutines are anonymous. But it can be convenient to
 * transfer data anonymously just after fork or before destroy.
 */
bool glas_data_xchg(glas* src, int amt, glas* dst);


/*********************************************
 * DATA TRANSFER
 *********************************************/
/** 
 * Push a copy of a binary to the data stack. 
 * 
 * In glas, a binary is logically a list of bytes. A list is formed
 * by a right spine of pairs terminating in unit, e.g. (1,(2,(3,()))).
 * In a binary, each element should be an integer in range 0..255.
 * (See integers below.)
 * 
 * The glas runtime optimizes list representations, and binaries even
 * more so. Thus, when first pushed, large binaries will be copied to
 * a runtime array.
 * 
 * In glas systems, texts are typically represented as binaries using
 * the utf-8 encoding. Thus, use binaries for texts, too.
 *
 * Note: there is a zero-copy variant; see below.
 */
void glas_binary_push(glas*, uint8_t const* buf, size_t len);

/**
 * Copy from binary at top of stack into client buffer.
 * For convenience, a large binary may be read in multiple steps.
 * 
 * Returns 'true' iff end-of-list is reached. There are several
 * reasons this operation might return false:
 * 
 * - more data, not done reading yet
 * - offset is beyond end-of-list
 * - not a pair structure after 'amt'
 * - list contains non-byte element
 * 
 * If buf is NULL, the actual copy step is skipped, but we'll
 * still scan to compute amt_read and the return value.
 * 
 * Note: there is a zero-copy variant; see below.
 */
bool glas_binary_peek(glas*, size_t start_offset, size_t max_read, 
    uint8_t* buf, size_t* amt_read);

/**
 * A callback essentially to 'free' an object, though it perform
 * a decref depending on context, or merely represent a signal. 
 * This is used when objects are referenced across the runtime
 * boundary (in either direction).
 * 
 * The release function may be NULL if no cleanup is required.
 */
typedef struct {
    void (*release)(void*);
    void *release_arg;
} glas_release_cb;

inline void glas_release(glas_release_cb cb) {
    if(NULL != cb.release) {
        cb.release(cb.release_arg);
    }
}

/**
 * Zero-copy push for binaries: a performance-safety tradeoff.
 *
 * Moving binaries by copy is safe, but incurs allocation and 
 * copy overheads. A zero-copy push is feasible, but the client
 * must not modify the binary while it is held by the runtime.
 * The client provides a callback for notification when the 
 * runtime releases the data. 
 * 
 * Note: Binaries may be copied regardless, e.g. if smaller than
 * a heuristic threshold. Release may be called before return.
 */
void glas_binary_push_zc(glas*, uint8_t const* buf, size_t len, glas_release_cb);

/**
 * Zero-copy reads. With some qualifications.
 * 
 * The glas runtime represents large binaries as ropes, logically
 * slicing and splicing fragments, heuristically allocating and 
 * copying as smaller chunks are composed.
 * 
 * The glas runtime may also flatten a binary upon request. And 
 * such an operation is idempotent, i.e. subsequent requests are 
 * trivial. This flatten step is still, in practice, a copy. But,
 * despite limitations, there is a real opportunity for zero copy
 * if the data was previously flattened. Or if it would be in the
 * future, in which case we can avoid rework.
 *  
 * As with zero-copy push, the client must not modify the data. In
 * this case the client *receives* a callback to notify the runtime
 * when done.
 * 
 * The return value is determined same as copying peek. And if ppBuf
 * is NULL, we still do everything except the actual copy and return,
 * including flattening the representation on the data stack. 
 * 
 * Note: If ppBuf is NULL, the release callback may also be NULL. 
 * If release callback is NULL but ppBuf is not, we'll set *ppBuf to
 * NULL then treat ppBuf as NULL.
 */
bool glas_binary_peek_zc(glas*, size_t start_offset, size_t max_read,
    uint8_t const** ppBuf, size_t* amt_read, glas_release_cb*);

/**
 * Push an integer to top of data stack.
 * 
 * Integers are represented by variable-width bitstrings, e.g. 13 is 
 * 0b1101, negatives use ones complement, i.e. -13 is 0b0010, and 
 * zero is the empty bitstring. In turn, bitstrings are a degenerate 
 * case of 'binary' trees where each node at most one edge: left or
 * right, zero or one.
 * 
 * This covers all bitstrings and integers, one-to-one. Treating any
 * specific bitstring as an integer is, thus, very contextual. Clients
 * may use integers to construct bitstrings
 */
#define glas_integer_push(a,b)      \
  _Generic((b),                     \
    int64_t: glas_i64_push,         \
    int32_t: glas_i32_push,         \
    int16_t: glas_i16_push,         \
    int8_t: glas_i8_push,           \
    uint64_t: glas_u64_push,        \
    uint32_t: glas_u32_push,        \
    uint16_t: glas_u16_push,        \
    uint8_t: glas_u8_push)(a,b)
void glas_i64_push(glas*, int64_t);
void glas_i32_push(glas*, int32_t);
void glas_i16_push(glas*, int16_t);
void glas_i8_push(glas*, int8_t);
void glas_u64_push(glas*, uint64_t);
void glas_u32_push(glas*, uint32_t);
void glas_u16_push(glas*, uint16_t);
void glas_u8_push(glas*, uint8_t);

/**
 * Copy an integer from top of data stack into client buffer.
 * Fails if target is not an integer or outside integer range.
 */
#define glas_integer_peek(a,b)      \
  _Generic((b),                     \
    int64_t*: glas_i64_peek,        \
    int32_t*: glas_i32_peek,        \
    int16_t*: glas_i16_peek,        \
    int8_t*:  glas_i8_peek,         \
    uint64_t*: glas_u64_peek,       \
    uint32_t*: glas_u32_peek,       \
    uint16_t*: glas_u16_peek,       \
    uint8_t*:  glas_u8_peek)(a,b)
bool glas_i64_peek(glas*, int64_t*);
bool glas_i32_peek(glas*, int32_t*);
bool glas_i16_peek(glas*, int16_t*);
bool glas_i8_peek(glas*, int8_t*);
bool glas_u64_peek(glas*, uint64_t*);
bool glas_u32_peek(glas*, uint32_t*);
bool glas_u16_peek(glas*, uint16_t*);
bool glas_u8_peek(glas*, uint8_t*);

/**
 * Push a symbol to the top of the data stack.
 * 
 * Within glas systems, symbols are a subset of bitstrings that meet
 * a few constraints:
 * 
 * - a bitstring consists of multiple 'octets' of 8 bits
 * - lowest 8 bits are all zeroes
 * - no other octet consist of all zeroes
 * 
 * Essentially, it's C strings (NULL terminated) encoded into bitstrings.
 * 
 * Symbols are primarily used in context of representing dictionaries
 * or labeled variants. It is assumed by the runtime that symbols are 
 * relatively short. There are no optimizations for large symbols.
 */
void glas_symbol_push(glas*, char const* symbol);

/**
 * Try to read a symbol from the top value of the data stack.
 * 
 * The value may be a labeled variant, i.e. the value following
 * the symbol doesn't need to be unit.
 */
bool glas_symbol_peek(glas*, size_t max_read, char* buf, size_t* amt_read);

/********************************
 * COMPUTATIONS AND OPERATIONS
 ********************************/

/**
 * Primitive Operations
 * 
 * The glas program model has six primitive data manipulations. In
 * this case 'failure' is represented by returning false.
 */
void glas_mkp(glas*);   // A B -- (A,B)     ; B is top of stack
void glas_mkl(glas*);   // X -- 0b0.X
void glas_mkr(glas*);   // X -- 0b1.X
bool glas_unp(glas*);   // (A,B) -- A B     ; may fail
bool glas_unl(glas*);   // 0b0.X -- X       ; may fail
bool glas_unr(glas*);   // 0b1.X -- X       ; may fail

/**
 * Non-modifying analyses. 
 */
bool glas_data_is_unit(glas*);      // ()
bool glas_data_is_pair(glas*);      // (A,B)
bool glas_data_is_inl(glas*);       // 0b0._
bool glas_data_is_inr(glas*);       // 0b1._
bool glas_data_is_list(glas*);      // () or (A, List)
bool glas_data_is_binary(glas*);    // List of 0..255
bool glas_data_is_bitstr(glas*);    // () or 0b0.Bits or 0b1.Bits 
bool glas_data_is_dict(glas*);      // byte-aligned radix-tree dicts
bool glas_data_is_ratio(glas*);     // dicts of form { n:Bits, d:Bits }
inline bool glas_data_is_integer(glas* g) {
    return glas_data_is_bitstr(g);
}

/**
 * List Operations
 */
bool glas_list_len(glas*);          // L -- L N         
bool glas_list_len_peek(glas*, size_t*); // for convenience
bool glas_list_split(glas*);        // (L++R) (L len) -- L R
bool glas_list_split_n(glas*, size_t); 
bool glas_list_append(glas*);       // L R -- (L++R)

/**
 * Bitstring Operations
 */
bool glas_bits_len(glas*);
bool glas_bits_split(glas*);
bool glas_bits_split_n(glas*, size_t);
bool glas_bits_append(glas*);
bool glas_bits_len_peek(glas*, size_t*);

/**
 * Dict Operations 
 * 
 * For convenience and performance, there are two forms for some APIs.
 * It is recommended to provide the symbol directly to the dictionary
 * operation rather than encoding the symbol as a separate step.
 */
bool glas_dict_insert(glas*);       // Item Record Symbol -- Record'
bool glas_dict_remove(glas*);       // Record Symbol -- Item Record'
bool glas_dict_insert_sym(glas*, char const* symbol); // Item Record -- Record'
bool glas_dict_remove_sym(glas*, char const* symbol); // Record -- Item Record'
// TBD: list of keys, count of items, splitting a dict on a symbol.


/**
 * Rationals. TBD
 */

/**
 * Arithmetic. TBD.
 */

/*****************************************
 * REGISTERS
 ****************************************/

/**
 * Introduce runtime-local registers.
 * 
 * This operation binds local registers to a prefix within the
 * namespace. This namespace is lexically scoped, thus only 
 * available to future operations upon or through the same 
 * context.
 * 
 * Registers do not need to be declared individually. Instead, 
 * each name under the prefix henceforth refers to a distinct
 * register. All registers are logically initialized to zero.
 * 
 * The runtime may heuristically garbage-collect and reconstruct 
 * registers that contain zeroes.
 * 
 * This operation may fail if the prefix shadows existing definitions,
 * unless shadowing checks are disabled in context.
 */
bool glas_reg_new(glas*, char const* prefix);

/**
 * Swap data between data stack and a register.
 * 
 * This is the only primitive operation on registers, supporting 
 * linear data. Fails if name does not refer to a register. But
 * we'll often use registers via accelerated operations to support
 * precise read-write conflict analysis, improving concurrency.
 */
bool glas_reg_rw(glas*, char const* register_name);

/**
 * Use register as simple data cell. 
 * 
 * These implicitly copy and drop data currently in the register,
 * thus may also fail if the cell contains linear data (and the
 * check is not suppressed in context).
 * 
 * A benefit of this API is that the runtime can more precisely
 * recognize read-write conflicts (without comparing values) 
 * compared to swapping the data.
 */
bool glas_reg_get(glas*, char const* register_name);
bool glas_reg_set(glas*, char const* register_name);

/**
 * View register as a queue.
 * 
 * A queue is a register containing a list but used under certain 
 * constraints: the reader cannot perform partial reads, and the
 * writer should not read any contents of the queue.
 * 
 * This allows for a single reader and multiple concurrent writers
 * without a read-write conflict. The writes can be deferred until
 * a logical commit order is determined. In distributed runtimes, 
 * we can also split the register between reader and writer nodes.
 * 
 * In theory, anyways. This runtime might not get around to fully
 * realizing those opportunities.
 * 
 * Queue operations may fail if the register does not contain a
 * list. Read fails if there is insufficient data. And peek may
 * fail if the queue contains linear data and linearity checks
 * are not disabled in context.
 */
bool glas_queue_read(glas*, char const* register_name); // N -- List
bool glas_queue_read_n(glas*, char const* register_name, size_t amt); // -- List
bool glas_queue_unread(glas*, char const* register_name); // List --  ; appends to head of queue
bool glas_queue_write(glas*, char const* register_name); // List -- ; appends to tail of queue

bool glas_queue_peek(glas*, char const* register_name); // N -- List ; read copy unread
bool glas_queue_peek_n(glas*, char const* register_name, size_t amt); // -- List

/**  
 * TBD: 
 * 
 * External registers (i.e. bound to a key-value database) 
 * 
 * Not sure I want to build this into the runtime directly. Instead,
 * we could feasibly support volumes of registers bound via callbacks.
 * But for performance, we'll probably need some specialized handling.
 * 
 * More specialized registers: bags, crdts, dict as key-value database, etc..
 */

/*******************************
 * NAMESPACES AND DEFINITIONS
 *******************************/



/** TBD
 * - interaction with transaction
 *   - query whether a transaction is still viable
 *   - callback hooks (precommit, commit, abort)
 * - access to bgcalls
 * - data abstraction
 * - access to other reflection APIs
 *   - logging
 *   - error info
 *   - etc.
 * - invoking operations through name on stack
 * - support for translations (array of structs of C strings?)
 * - binding register spaces (local, persistent)
 * - importing definitions (user config, command strings)
 */


#if 0

/**
 * Attempt to load configuration from default locations.
 * 
 *   ${GLAS_CONF} environment variable  (if defined)
 *   ${HOME}/.config/glas/conf.glas     (on Linux)
 *   %AppData%\glas\conf.glas           (in Windows, eventually)
 */
bool glas_apply_user_config(glas_rt*);
// TBD: configure from specified file or configuration text

/**
 * Load and run an application defined in the configuration.
 */
bool glas_run(glas_rt*, char const* appname, 
    int argc, char const* const* argv);

#endif

/*****************************************
 * NAMESPACE ACCESSORS
 *****************************************/

/**
 * Returns true if a specific name is defined in view
 * of the context, otherwise false.
 */ 
bool glas_name_defined(glas*, char const* name);

/**
 * Returns 
 */
bool glas_prefix_inuse(glas*, char const* prefix);

/**
 * To support flexible interactions, the client may define
 * parts of the namespace with callbacks.
 * 
 * Each callback presents as an `Env -> Program` type to the
 * runtime. In this case, the Env input provides access to
 * the caller's environment, controlled by the caller.
 * 
 * This extended Env is presented as a 'glas*' context that is
 * valid only for duration of the callback. A prefix to access
 * the caller's environment can be specified when the callback
 * is first defined.
 * 
 * Aside from further interaction with the caller, this 'glas*'
 * context supports transaction hooks, yielding (if not atomic),
 * or failing a step. The client could write definitions that
 * perform most activity between commits.
 * 
 * Returning 'false' will represent failure. To represent 
 * divergence, apply the error operation.
 */
typedef bool (*glas_prog_cb)(void* , glas*);

/**
 * Insert a callback-based definition into the environment.
 * 
 * This callback is lexically scoped, i.e. definitions added
 * after are not visible via the 'glas*' callback parameter.
 * However, the callback may access the caller's environment
 * through an indicated prefix.
 * 
 * Parameters:
 *   defname - the name to define
 *   glas_def_cb - the callback
 *   void* - arbitrary callback context.
 *   caller_prefix - e.g. "$" to access client register "$foo".
 *       May be NULL if you don't need the caller's context.
 */
void glas_define_by_callback(glas*, char const* defname, 
    glas_prog_cb, void*, char const* caller_prefix);


/**
 * hook for transaction events?
 *   e.g. init, precommit, commit, abort
 */

/**
 * This returns true if the context is inside an atomic transaction,
 * whether client-generated or via callback, otherwise false.
 */
bool glas_context_is_atomic(glas*);


/**
 * Clients may initiate transactions. Conceptually, each 'glas*' context
 * may support a stack of hierarchical transactions. 
 */


/***************************
 * ERROR HANDLING
 **************************/
/** 
 * query for errors, browse errors
 * perhaps clear errors
 */

/************************************************
 * SYNCHRONIZATION
 ***********************************************/

/**
 * - awaiting changes to registers is viable, with timeouts
 */



#define GLAS_H
#endif


