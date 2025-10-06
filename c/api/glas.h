/**
 * Use glas runtime as a library.
 * 
 * Upon glas_cx_new(), the client (your program) receives a `glas*`
 * context. This context represents access to a glas coroutine that 
 * starts with an empty namespace and data stack. Clients can push 
 * or pop binaries and bitstrings, introduce registers, and define 
 * or invoke names.
 * 
 * In glas, coroutines are transactional, committing upon yield. A 
 * failure or error can be backtracked. Clients receive the same 
 * experience: error handling may be deferred to commit boundaries,
 * and is often handled by backtracking and trying something else.
 * For operations that cannot be undone, the client commits to a
 * future action.
 * 
 * The runtime supports clients in loading glas programs from source
 * files. A client may override and extend the file loader. Clients
 * interact with running programs by two means: shared registers and
 * callbacks.
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
 * - a coroutine, controlled through this API
 * - a data stack (and auxilliary stash)
 * - a namespace of definitions and registers
 * - bookkeeping for transactions, errors, etc..
 */
typedef struct glas glas;

/**
 * Create a new glas context.
 * 
 * Starts with an empty namespace, stack, and stash.
 */
glas* glas_cx_new();

/**
 * Drop the glas context.
 * 
 * This tells the runtime that the client is done with this context,
 * and that the associated coroutine has terminated.
 * 
 * The runtime may log some warnings if this results in orphaning
 * of linear data.
 */
void glas_cx_drop(glas*);

/**
 * Fork context for concurrency.
 * 
 * The fork receives a copy of its origin's namespace, and an optional
 * transfer from the data stack. The client may use this in a separate
 * thread or interleave operations.
 * 
 * Important Notes:
 * 
 * A fork cannot commit a step before its origin commits the step that
 * created the fork. The fork may be invalidated if its own creation is
 * aborted, becoming a dead branch. Commit before use in most cases.
 * 
 * Although forks of callback contexts are permitted, the caller will
 * block until all forks terminate, i.e. fork-join semantics.
 */
glas* glas_cx_fork(glas*, uint8_t stack_transfer);

/**
 * Choose between outcomes.
 * 
 * This is useful to model concurrent search or race conditions. Each
 * context receives a copy of the namespace, stack, and stash. However,
 * only one may commit. The first to yield becomes committed choice.
 * The other is invalidated, becoming a dead branch.
 */
glas* glas_cx_choice(glas*);

/*************************
 * TRANSACTIONS
 ************************/

/**
 * Commit step.
 * 
 * In glas coroutines, every yield-to-yield step is ideally an atomic,
 * isolated transaction. Upon yield, pending updates are applied, and
 * updates from concurrent operations may be observed.
 * 
 * However, a step may fail to commit. Concurrent changes may conflict.
 * This aligns with optimistic concurrency control: small transactions, 
 * retry as needed, heuristic scheduling to reduce rework. To control
 * data contention, perhaps introduce a queue.
 * 
 * The glas context can be rolled back to prior yield and retried. The
 * client should defer anything difficult to undo until after yield, 
 * awaiting commitment to action.
 * 
 * Aside from conflict, yield will also refuse to proceed in case of
 * errors. Errors are logically treated as forms of divergence, like 
 * infinite loops, thus cannot be committed. The client may check for
 * errors at any time, but after yield fails is a clear opportunity.
 * 
 */
bool glas_step_commit(glas*);

/**
 * Abort the current step. Rewind context to state at start of step.
 */
void glas_step_abort(glas*);

/**
 * Hierarchical transactions. (Omit.)
 * 
 * The glas program model has '%atomic' sections to support expression
 * of concurrency within transactions. However, I don't have a clear 
 * use case for the client. It only adds complexity to this API.
 */

/**
 * Checkpoint. (Tentative.)
 * 
 * Checkpoints would provide intermediate locations for rollback and
 * retry between yields. However, I don't have a clear vision on how
 * the client would integrate this, nor how to efficiently implement.
 * 
 * Not a high priority. 
 */

 /**
  * Post-Commit Actions.
  *
  * This method simplifies some common patterns preparing operations
  * to run after commit.
  * 
  * If the step commits, the operations are written into named queues,
  * preserving logical transaction commit order. The runtime spins up
  * worker threads to process the queues.
  * 
  * If the step aborts, the 'cancel' actions run during abort (unless
  * NULL). The primary role is recycle memory associated with 'arg'.
  */
void glas_step_postop(glas*, char const* op_queue,
    void (*op)(void* arg), void* arg, void (*cancel)(void* arg));
  

/***************************************
 * ERRORS 
 **************************************/

/**
 * Error Summary. A bitwise `OR` of error flags.
 * 
 * Logically, errors are divergence. They cannot be committed. But a
 * client may abort the step and retry, or try something else. A few
 * errors, such as name shadowing or assertions, can be suppressed by
 * specific means.
 */
typedef enum GLAS_ERROR_FLAGS {
    GLAS_NO_ERRORS          = 0x0000000,   
    GLAS_E_UNDERFLOW        = 0x0000001, // data stack underflow
    GLAS_E_DEAD_BRANCH      = 0x0000002, // pruned fork or choice context
    GLAS_E_LINEARITY        = 0x0000004, // copy or drop of linear data
    GLAS_E_EPHEMERALITY     = 0x0000008, // short-lived data in long-lived register
    GLAS_E_DATA_SEAL        = 0x0000010, // error working with sealed data
    GLAS_E_DATA_TYPE        = 0x0000020, // e.g. list append with not-a-list
    GLAS_E_NAME_SHADOW      = 0x0000100, // defined name was hidden 
    GLAS_E_NAME_UNDEF       = 0x0000200, // called an undefined name
    GLAS_E_NAME_TYPE        = 0x0000400, // e.g. call a non-program, set a non-reg
    GLAS_E_ASSERT           = 0x0001000, // assertion failure in running program
    GLAS_E_ERROR_OP         = 0x0002000, // use of '%error' in running program
    GLAS_E_ATOMIC_CB        = 0x0004000, // you tried to commit an atomic CB
    GLAS_E_SIGKILL          = 0x0010000, // operation killed willfully
    GLAS_E_QUOTA            = 0x0020000, // operation killed heuristically
    GLAS_E_CONFLICT         = 0x0040000, // lost a conflict with a concurrent operation 
    GLAS_E_CLIENT1          = 0x0100000, // a user may inject errors
    GLAS_E_CLIENT2          = 0x0200000, //             .
    GLAS_E_CLIENT3          = 0x0400000, //             .
    GLAS_E_CLIENT4          = 0x0800000, //             .
} GLAS_ERROR_FLAGS;

GLAS_ERROR_FLAGS glas_error_get(glas*);         // read error flags
void glas_error_set(glas*, GLAS_ERROR_FLAGS);   // adds to error flags (monotonic)


// Note on hierarchical transactions:
//  interaction with yield requires some attention.
//  e.g. commit only applies upon next yield.



/***************************************
 * DATA STACK MANIPULATION
 ***************************************/

void glas_data_swap(glas*); // A B -- B A

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
 */
void glas_data_stash(glas*, int amt);

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
 * Zero-copy reads. (With some qualifications.)
 * 
 * The glas runtime represents large binaries as ropes, logically
 * slicing and splicing fragments, heuristically allocating and 
 * copying as smaller chunks are composed.
 * 
 * However, the glas runtime may flatten a binary upon request, 
 * e.g. via program annotations or use of this zero-copy read API.
 * This flatten operation is a copy, but it's also stable and 
 * idempotent. Clients can zero-copy read if already flattened,
 * or to avoid redundant flatten steps later.
 * 
 * As with zero-copy push, the client must not modify the data. When
 * done, the client should run the returned release callback.
 */
bool glas_binary_peek_zc(glas*, size_t start_offset, size_t max_read,
    uint8_t const** ppBuf, size_t* amt_read, glas_release_cb* pfree);

/**
 * Push an integer to top of data stack.
 * 
 * Note: Integers are represented as variable-width bitstrings.
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
 * Push a binary as a bitstring. Writes full octets.
 */
void glas_bits_push(glas*, uint8_t const* buf, size_t len);

/**
 * Read a bitstring as a binary. Reads full octets.
 */
bool glas_bits_peek(glas*, size_t start_offset, size_t max_read, 
    uint8_t* buf, size_t* amt_read);





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
bool glas_unp(glas*);   // (A,B) -- A B | FAIL
bool glas_unl(glas*);   // 0b0.X -- X   | FAIL
bool glas_unr(glas*);   // 0b1.X -- X   | FAIL

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

/**
 * List Operations
 */
void glas_list_len(glas*);          // L -- L N         
void glas_list_len_peek(glas*, size_t*); // for convenience
void glas_list_split(glas*);        // (L++R) (L len) -- L R
void glas_list_split_n(glas*, size_t); 
void glas_list_append(glas*);       // L R -- (L++R)

/**
 * Bitstring Operations
 */
void glas_bits_len(glas*);
void glas_bits_split(glas*);
void glas_bits_split_n(glas*, size_t);
void glas_bits_append(glas*);
void glas_bits_len_peek(glas*, size_t*);

/**
 * Dict Operations 
 * 
 * For convenience and performance, there are two forms for some APIs.
 * It is recommended to provide the label directly to the dictionary
 * operation rather than encoding the label as a separate step.
 */
void glas_dict_insert(glas*);       // Item Record Label -- Record'
bool glas_dict_remove(glas*);       // Record Label -- Item Record' | FAIL
void glas_dict_insert_l(glas*, char const* label); // Item Record -- Record'
bool glas_dict_remove_l(glas*, char const* label); // Record -- Item Record' | FAIL
// TBD: effecient iteration over dicts, merge of dicts

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
 * Introduce new registers.
 * 
 * Binds a set of registers to a prefix in the namespace. Each name
 * under the prefix then refers to a separate register. Registers hold
 * the zero value until written. 
 */
void glas_reg_new(glas*, char const* prefix);

/**
 * Swap data between data stack and a register.
 */
void glas_reg_rw(glas*, char const* register_name);

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
void glas_reg_get(glas*, char const* register_name);
void glas_reg_set(glas*, char const* register_name);

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
 * In theory, anyways. This runtime might never get around to fully
 * realizing those opportunities.
 * 
 * Queue operations may fail if the register does not contain a
 * list. Read fails if there is insufficient data. And peek may
 * fail if the queue contains linear data and linearity checks
 * are not disabled in context.
 */
void glas_queue_read(glas*, char const* register_name); // N -- List
void glas_queue_read_n(glas*, char const* register_name, size_t amt); // -- List
void glas_queue_unread(glas*, char const* register_name); // List --  ; appends to head of queue
void glas_queue_write(glas*, char const* register_name); // List -- ; appends to tail of queue

void glas_queue_peek(glas*, char const* register_name); // N -- List ; read copy unread
void glas_queue_peek_n(glas*, char const* register_name, size_t amt); // -- List

/**  
 * TBD: 
 * 
 * External registers (i.e. bound to a key-value database) 
 * 
 * Not sure I want to build this into the runtime directly. Instead,
 * we could feasibly support volumes of registers bound via callbacks.
 * But for performance, we'll probably need some specialized handling.
 * 
 * More specialized registers: bags, crdts, dict reg as kvdb, etc..
 */

/*******************************
 * NAMESPACES AND DEFINITIONS
 *******************************/



/** TBD
 * - access to bgcalls
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

/**
 * Returns true if a specific name is defined in view
 * of the context, otherwise false.
 */ 
bool glas_name_defined(glas*, char const* name);

/**
 * Returns true if a prefix contains at least one name.
 */
bool glas_prefix_inuse(glas*, char const* prefix);


/**
 * Enable shadowing for the next name or prefix.
 * 
 * This flag is cleared when the next name or prefix is defined. This
 * is intended to resist accidents, ensuring shadowing is explicit in
 * the client program. If not enabled, name shadowing is an error.
 */
void glas_name_shadow(glas*);


/**
 * A client may define names with callbacks.
 * 
 * Each callback presents as an `Env -> Program` type to the runtime.
 * The Env parameter provides access to the caller's environment. The
 * caller may restrict Env.
 * 
 * The glas* callback context provides access to the caller's Env via
 * specified prefix, unless NULL. Other names will bind to a copy of
 * the namespace at time of definition (i.e. lexically scoped).
 * 
 * Returning 'false' represents failure, not error. 
 * 
 * Assumed to multi-thread safe.
 */
typedef struct {
    bool (*operation)(glas*, void* cbarg);
    void (*release)(void* cbarg);
    void* cbarg;                // opaque, passed to operation
    char const* caller_prefix;  // e.g. "$" so "$cb" calls back to caller.
    uint8_t ar_in, ar_out;      // data stack arity restrictions
    bool atomic;                // doesn't yield; no `glas_step_commit`.
} glas_prog_cb;

void glas_define_by_callback(glas*, char const* name, glas_prog_cb);





#define GLAS_H
#endif


