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
 * 
 * Each context should be used in a single-threaded manner, though
 * they aren't sticky: contexts may be migrated across OS threads.
 * Operations on separarate contexts are fully mt-safe.
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
 * i.e. that no further client commands are incoming. This does not
 * mean the runtime is done with the coroutine. The runtime may try
 * `glas_step_commit`, then `glas_step_abort` if commit fails. 
 */
void glas_cx_drop(glas*);

/**
 * Fork context for concurrency.
 * 
 * The fork receives a copy of its origin's namespace, and an optional
 * transfer from the data stack. 
 * 
 * Important Notes:
 * 
 * A fork cannot commit before its origin commits. The origin may yet 
 * abort the step that created the fork. In most cases, commit before 
 * starting the fork.
 * 
 * In a callback context, all forks must terminate before the caller 
 * continues, i.e. fork-join semantics.
 */
glas* glas_cx_fork(glas*, uint8_t stack_transfer);


/**
 * Regarding integration with non-deterministic choice:
 * 
 * In general, choices exist only *within* steps. The moment we commit
 * to a step, we have committed choices. In theory, a glas system may
 * defer unobserved across steps, but it's complicated (entanglement!)
 * and isn't required by our semantics.
 *  
 * The simplest option to integrate non-deterministic choice is to try 
 * other choices on abort. We can also provide simple operations, e.g. 
 * select an integer smaller than N, or pick a value from a list.
 * 
 * But it might be interesting to let clients explore choices more
 * precisely, expanding a single context into several. I'm not sure
 * how to best express this, however. Perhaps as a parallel callback?
 */

/***************************
 * MEMORY MANAGEMENT
 **************************/

/**
 * A runtime hold on client resources (or vice versa).
 * 
 * This is currently used for:
 * 
 * - definition of callbacks
 * - zero-copy binary transfers
 * - foreign pointers; client ADTs
 * 
 * When the associated resource falls out of scope, the pin should be
 * released. The pin may refer to a reference-count object or similar
 * instead of directly to the resource in question.
 * 
 * If no cleanup is needed, set release function to NULL.
 */
typedef struct {
    void (*release)(void* resource);
    void *resource;
} glas_pin;

inline void glas_release(glas_pin cb) {
    if(NULL != cb.release) {
        cb.release(cb.resource);
    }
}

/************************
 * NAMESPACES
 ************************/

/**
 * Load primitive definitions.
 * 
 * This loads the program-model primitives under a specified prefix,
 * conventionally "%". This doesn't include names managed by front-end
 * compilers, e.g. %src, %env, %arg, %self.
 * 
 * TBD: Variations to override and extend %macro compile-time effects,
 * or at least load from a virtualized system.
 */
void glas_load_prims(glas*, char const* at_prefix);

/**
 * Load a configuration.
 * 
 * This operation loads a configuration file:
 * 
 * - fixpoint bindings of %self and %env
 * - provide %arg for runtime version info 
 * - bootstrap of %env.lang.glas (and maybe %env.lang.glob) compilers
 * 
 * It's difficult to tease this knot apart, and it's convenient to
 * have it as one big op. 
 */
void glas_load_config(glas*, char const* at_prefix, char const* prims_prefix);

/**
 * TODO:
 * - load script bound to configuration and primitives
 */

/**
 * Prefix-to-prefix Translations.
 * 
 * The glas namespace model uses prefix-to-prefix translations for
 * many purposes, and the client may also make use of them. Within
 * C, we'll simply represent them as an array of pairs of strings,
 * where the RHS may be NULL, terminating in `{ NULL,  NULL}`.  
 * 
 *    { {"foo.", "bar."}, { "$", NULL }, { NULL, NULL } }
 * 
 * In each case, the LHS represents a match prefix, the RHS the update.
 * The RHS may be NULL, indicating that a prefix is hidden. Only the
 * longest matching prefix is applied to each name.
 * 
 * For performance, the runtime may compose translations or cache the
 * bindings. Either way, translation overhead is negligible.
 */
typedef struct { char const *lhs, *rhs; } glas_tl; 

/**
 * Translate client view of namespace.
 * 
 * The specified translation is applied to referencing names currently
 * in the namespace. For example, if we translate `{ "bar.", "foo." }`
 * then a future call (or register operation) on "bar.x" will instead
 * refer to "foo.x". Note: This also translates "bar" to "foo" due to
 * the implicit translation suffix, but not "bard" to "food".
 * 
 * This does not influence future definitions.
 */
void glas_ns_view(glas*, glas_tl const*);

/**
 * Returns true if a specific name is defined.
 */ 
bool glas_ns_has_def(glas*, char const* name);

/**
 * Returns true if a prefix contains at least one name.
 */
bool glas_ns_has_prefix(glas*, char const* prefix);


/**
 * Define programs via callbacks.
 * 
 * Each invocation receives a 'glas*' callback context, valid for the
 * duration of the callback and implicitly dropped upon return. 
 * 
 * This context provides access to the caller's environment through a
 * specified prefix (e.g. "$"), and the host's lexical environment via
 * another (often "", accepting some name shadowing). The caller's data
 * stack is also visible up to input arity.
 * 
 * To simplify the API, callbacks are marked atomic or not:
 * 
 * - An atomic callback cannot commit or abort steps, and cannot fork.
 *   It may defer operations via on_commit and on_abort.
 * - A non-atomic callback may commit steps, but cannot be called from
 *   %atomic sections within a program or atomic invocation.
 * 
 * Either way, a callback context may become a dead branch if caller is
 * aborted. When this happens, a client should return false ASAP. But a 
 * non-atomic callback is safe from this after first step commit.
 * 
 * The glas system distinguishes errors and failures. Callbacks report
 * failure by returning false, and errors by adding flags to context.
 * 
 * In general, callbacks must be multi-threading safe.
 */
typedef struct {
    bool (*operation)(void* client_arg, glas*);
    void* client_arg;           // opaque, passed to operation
    char const* caller_prefix;  // e.g. "$"
    char const* host_prefix;    // e.g. "" (caller shadows "$") 
    uint8_t ar_in, ar_out;      // data stack arity assumptions
    bool atomic;
} glas_prog_cb;

void glas_def_by_cb(glas*, char const* name, glas_prog_cb, glas_pin);


/*************************
 * TRANSACTIONS
 ************************/

/**
 * Commit current step.
 * 
 * In glas coroutines, every yield-to-yield step is ideally an atomic,
 * isolated transaction. Each step is committed: pending updates are
 * applied, and updates from concurrent operations may be observed.
 * 
 * Clients receive a similar experience. Until they commit a step, the
 * updates are local, and a client operates on a stable view of shared
 * registers. Though, invoked programs may 'yield', committing a step.
 * 
 * A step may fail to commit, typically due to error or conflict. The
 * glas system uses optimistic concurrency control: Resources are not
 * locked down. Instead, users keep transactions small and avoid data
 * contention; retry until successful. Queues can mitigate conflicts. 
 * 
 * When commit fails, the client may abort the step, return to a prior
 * state and retry. However, most languages are not designed for this
 * style of use, including C. To keep it simple, the client may commit
 * prior to any operations that are difficult to undo, i.e. so we have
 * committed action. The runtime supports deferred callback actions.
 * 
 * A client is not required to retry the same operation after a step
 * fails to commit.
 */
bool glas_step_commit(glas*);

/**
 * Abort the current step. 
 * 
 * This always rewinds context to the last committed step.
 */
void glas_step_abort(glas*);

/**
 * Committed action.
 * 
 * For operations that are difficult to undo, clients may defer action 
 * until after commit. This prevents observation of results within the
 * transaction, but it greatly simplifies integration.
 * 
 * To preserve ordering, transactions may write operations into queues.
 * These queues will be processed by runtime worker threads. The NULL
 * queue will be handled locally, e.g. within `glas_step_commit`. This
 * is useful for resources owned by the thread, or for synchronization,
 * waiting on workers to complete certain steps.
 * 
 * Notes: 
 * - Use `glas_load_opqueues` to prepare to access opqueues.
 * - Consider use of on_abort to clean up allocations for arg.
 */
void glas_step_on_commit(glas*, void (*op)(void* arg), void* arg, 
    char const* opqueue);

/**
 * Prepare for `glas_on_commit` by binding queues into namespace.
 * 
 * The operation queues used by on_commit are runtime global, but they
 * are not implicitly bound to the namespace. Modeling them as part of
 * the namespace allows for flexible translation and scoping.
 * 
 * Similar to register spaces, each name implicitly refers to a distinct
 * queue.
 */
void glas_load_opqueues(glas*, char const* at_prefix);

/**
 * Defer operations until abort.
 * 
 * This is useful to clean up memory allocated for on_commit, but it
 * may find other use cases. 
 */
void glas_step_on_abort(glas*, void (*op)(void* arg), void* arg);

/**
 * A common coupling of on_commit and on_abort.
 * The abort operation is represented by the pin.
 */
inline void glas_step_defer(glas* g, void (*op)(void* arg), void* arg,
    char const* opqueue, glas_pin p)
{
    glas_step_on_abort(g, p.release, p.resource);
    glas_step_on_commit(g, op, arg, opqueue);
}

/**
 * Hierarchical transactions. (Omit.)
 * 
 * No clear use case for the client. A lot of added complexity. Let's
 * just skip support for hierarchical transactions at this layer.
 */

/**
 * Checkpoints. (Tentative.)
 * 
 * We could feasibly support partial rollback of a context within a
 * step, instead of always retrying the full step. But I think this
 * feature should wait until I have a clearer vision for it, and a
 * better understanding of the cost.
 * 
 * Not a high priority, at the moment.
 */

/**
 * Virtual Registers (Tentative.)
 * 
 * We could introdues runtime-global virtual registers, bind them to
 * the namespace, for purpose of fine-grained read-write conflict
 * analysis as clients work with these resources. 
 * 
 * glas_load_vreg(glas*, char const* at_prefix);
 * glas_vreg_rw/get/set/read/write(glas*, char const* vreg_name);
 * 
 * However, it isn't clear to me how to effectively leverage this.
 * Will probably need to build something that pushes the boundaries
 * of what is possible with just on_commit and on_abort.
 */


/***************************************
 * ERRORS 
 **************************************/

/**
 * Error Summary. A bitwise `OR` of error flags.
 * 
 * Logically, errors are divergence. They cannot be committed. Error
 * handling is instead based on rewinding via `glas_step_abort` and
 * retrying, or trying something different.
 * 
 * In context of state changes and non-deterministic choice, retrying
 * even assertion errors may succeed.
 * 
 * The 'dead branch' error is a special case. A context cannot recover
 * from this error: it represents that the creation of the context is
 * itself aborted. Only thing to do is drop the context and move on.
 */
typedef enum GLAS_ERROR_FLAGS {
    GLAS_NO_ERRORS          = 0x0000000,   
    GLAS_E_CONFLICT         = 0x0000001, // concurrency conflicts; retry might avoid
    GLAS_E_DEAD_BRANCH      = 0x0000002, // context creation was aborted; fork or callback
    GLAS_E_SIGKILL          = 0x0000004, // operation killed willfully; clear explicitly
    GLAS_E_QUOTA            = 0x0000008, // operation killed heuristically
    GLAS_E_ERROR_OP         = 0x0000010, // use of '%error' in running program
    GLAS_E_ASSERT           = 0x0000020, // assertion failure in running program
    GLAS_E_UNDERFLOW        = 0x0000100, // data stack underflow
    GLAS_E_ATOMICITY        = 0x0000200, // no commit in atomic program callback
    GLAS_E_LINEARITY        = 0x0001000, // copy or drop of linear data
    GLAS_E_EPHEMERALITY     = 0x0002000, // short-lived data in long-lived register
    GLAS_E_SEALED           = 0x0004000, // failed to unseal data before use, or wrong seal
    GLAS_E_UNDEFINED        = 0x0020000, // call an undefined name
    GLAS_E_TYPE             = 0x0040000, // generic runtime type errors
    GLAS_E_CLIENT           = 0x1000000, // user-defined error
} GLAS_ERROR_FLAGS;

GLAS_ERROR_FLAGS glas_errors_get(glas*);         // read error flags
void glas_errors_add(glas*, GLAS_ERROR_FLAGS);   // bitwise 'or' to error flags (monotonic)

/***************************************
 * DATA STACK
 ***************************************/

 /**
  * Basic Data Manipulations.
  * 
  * The glas program model has a few simple operations: %copy, %drop, 
  * %swap, and %dip. But this assumes a compiler will eliminate these
  * operations, reducing the stack to logical locations. For this API,
  * we'll provide a few bulk ops.
  */
void glas_data_copy(glas*, uint8_t amt); // A B -- A B A B ; copy 2
void glas_data_drop(glas*, uint8_t amt); // A B C -- A ; drop 2

/**
 * Move data to or from an auxilliary stack, called the stash.
 * 
 * If amt > 0, moves data to the stash. If amt < 0, transfers |amt| 
 * from stash to data stack. Equivalent to moving one item at a time.
 * 
 * Note: The stash serves the role of %dip in the program model.
 */
void glas_data_stash(glas*, int8_t amt);

/**
 * Visualize data shuffling based on a simple moves string.
 *   
 *   "abc-abcabc"   copy 3
 *   "abc-b"        drops a and c
 *   "abcd-abcab"   drops d, copies ab to top of stack
 * 
 * This operation will navigate to '-', scan leftwards popping items
 * into local variables [a-zA-Z]. Then it scans rightwards from '-',   
 * pushing variables back onto the stack. It's an error if the string
 * reuses variables in LHS, refers to unassigned variables in RHS, or
 * lacks the '-' separator, or is otherwise malformed We'll also detect
 * linearity errors.
 * 
 * For complex operations, this compact bit of syntax is likely more
 * efficient than performing the copies, swaps, stashes, and drops.
 * It's also far more comprehensible. OTOH, if you feel a frequent
 * need for this op, consider refactoring or more registers.
 */
bool glas_data_move(glas*, char const* moves);

/*********************************************
 * DATA TRANSFER
 *********************************************/
/** 
 * Push a binary to the data stack.
 * 
 * The base version will copy the binary. The zero-copy (_zc) variant
 * will transfer a reference. Small binaries may be copied regardless.
 * The client must treat zero-copy data as immutable until released.
 * 
 * The runtime logically treats binaries as a list of small integers, 
 * but the representation is heavily optimized.
 */
void glas_binary_push(glas*, uint8_t const*, size_t len);
void glas_binary_push_zc(glas*, uint8_t const*, size_t len, glas_pin);

/**
 * Non-destructively read binary data from top of data stack.
 * 
 * The base version will copy the binary. The zero-copy (_zc) variant
 * may need to flatten part of a rope-structured binary, then returns
 * a pinned reference. The client must not modify the latter memory,
 * and must release the pin when done.
 * 
 * Flattening a binary involves a copy. However, the zero-copy variant
 * is useful if the data was already flattened, would be in the future,
 * or will be requested many times.
 * 
 * The peek operation returns 'true' if end-of-list was reached and
 * everything available was read. The operation will return false with 
 * a partial result if data is only partially a valid binary.
 * 
 * Note: If buf/ppBuf is NULL, the runtime still attempts to produce
 * valid results for amt_read and return value, and flatten for _zc. 
 */
bool glas_binary_peek(glas*, size_t start_offset, size_t max_read, 
    uint8_t* buf, size_t* amt_read);
bool glas_binary_peek_zc(glas*, size_t start_offset, size_t max_read,
    uint8_t const** ppBuf, size_t* amt_read, glas_pin*);

/**
 * Push and peek bitstrings as binaries.
 * 
 * In this case, the binary is encoded as a bitstring of octets, i.e.
 * of a length multiple of eight. 
 * 
 * There are no zero-copy variants. Bitstrings are compactly encoded,
 * but not as binaries. The runtime assumes most bitstrings will be
 * short, e.g. integers or keys for a radix-tree dict.
 */
void glas_bitstr_push(glas*, uint8_t const* buf, size_t len);
bool glas_bitstr_peek(glas*, size_t start_offset, size_t max_read, 
    uint8_t* buf, size_t* amt_read);

/**
 * Push and peek for integers.
 * 
 * Integers are represented by variable-width bitstrings, msb to lsb, 
 * with negatives as the ones complement (just flip the bits):
 * 
 *      Int      Bits
 *       42     101010
 *       12       1100
 *        7        111
 *        0              (empty)
 *       -7        000
 *      -12       0011
 *      -42     010101
 * 
 * These numeric operators will perform the conversion to and from the
 * C representations of integers. The peek operations may fail if the
 * data is not a bitstring or if the integer is out of range.
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

void glas_i64_push(glas*, int64_t);
void glas_i32_push(glas*, int32_t);
void glas_i16_push(glas*, int16_t);
void glas_i8_push(glas*, int8_t);
void glas_u64_push(glas*, uint64_t);
void glas_u32_push(glas*, uint32_t);
void glas_u16_push(glas*, uint16_t);
void glas_u8_push(glas*, uint8_t);

bool glas_i64_peek(glas*, int64_t*);
bool glas_i32_peek(glas*, int32_t*);
bool glas_i16_peek(glas*, int16_t*);
bool glas_i8_peek(glas*, int8_t*);
bool glas_u64_peek(glas*, uint64_t*);
bool glas_u32_peek(glas*, uint32_t*);
bool glas_u16_peek(glas*, uint16_t*);
bool glas_u8_peek(glas*, uint8_t*);

/**
 * Pointers
 * 
 * The runtime treats pointers as an abstract, ephemeral data type. 
 * The client can push and peek pointers, and use pins for memory 
 * management. Useful for callback-based APIs.
 */
void glas_ptr_push(void*, glas_pin);
bool glas_ptr_peek(void**, glas_pin*);


/****************
 * DATA SEALING
 ****************/
// seal or unseal with reference to a register, opqueue, vreg - any identity source
// linearity option

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
void glas_reg_new(glas*, char const* at_prefix);

/**
 * Associative registers.
 * 
 * Binds a space of registers identified by a directed edge between two 
 * registers. Supports abstract data environments in callbacks APIs.
 */
void glas_reg_assoc(glas*, char const* r1, char const* r2, char const* at_prefix);

/**
 * Swap data between data stack and a register.
 */
void glas_reg_rw(glas*, char const* register_name); // A -- B

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
void glas_reg_get(glas*, char const* register_name); // -- A
void glas_reg_set(glas*, char const* register_name); // A --

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



#define GLAS_H
#endif


