/**
 * Use glas runtime as a library.
 * 
 * Upon glas_cx_new(), the client (your program) receives a `glas*`
 * context. This context represents a glas coroutine that starts with
 * an empty namespace, stack, and auxilliary stash.
 * 
 * Information exchange between client and runtime is restricted to 
 * binaries, bitstrings, and client pointers (as abstract data). The
 * stack may contain wider varieties of data, including definitions
 * or reified namespace environments.
 * 
 * Error handling is transactional: the client performs a sequence of
 * operations on a context then commits the step. In case of error or
 * conflict, the step fails to commit. But we can rewind and retry, or
 * try something else. The on_commit and on_abort callbacks simplify 
 * integration.
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
 * Reference to a glas context.
 * 
 * Each glas context is a logical thread or coroutine, having its own
 * stack, auxilliary stash, and a lexically scoped namespace. There is
 * also bookkeeping for transactions and errors.
 * 
 * Operations on separarate contexts are mt-safe. Individual contexts
 * are not: they must be used from only one thread at a time.
 */
typedef struct glas glas;

/**
 * Create a new glas context.
 * 
 * Starts with empty namespace, stack, and stash, and a decent default
 * adapter for definitions. Next step is usually to build the namespace.
 */
glas* glas_cx_new();

/**
 * Drop the glas context.
 * 
 * This tells the runtime that the client is done with this context,
 * i.e. that no further client commands are incoming. The final step
 * will be implicitly aborted.
 */
void glas_cx_drop(glas*);

/**
 * Create concurrent contexts.
 * 
 * A new fork receives a copy of its origin's namespace and an initial
 * transfer from the origin data stack. After creation, forks interact
 * through shared registers.
 * 
 * The origin may abort the step that created the fork. If so, the fork
 * will have a persistent 'uncreated' error and cannot commit. Consider 
 * committing to creation of a fork before spawning a thread to run it.
 * 
 * In callback contexts, forks must terminate (glas_cx_drop) before the
 * caller can continue, aka fork-join semantics. Forks are unusable in  
 * atomic callbacks because they must drop before commit.
 */
glas* glas_cx_fork(glas*, uint8_t stack_transfer);

/**
 * Concurrent search of non-deterministic choice.
 * 
 * Contexts can already search non-deterministic choices: evaluate, make
 * choices, reach error or failure, rollback and retry. But each `glas*` 
 * context is single-threaded. 
 * 
 * Ideally, we can explore many choices concurrently. To support this,
 * the runtime clones origin and supplies worker threads. A clone starts 
 * with a copy of origin's state and is evaluated within a callback. The
 * runtime transfers final state of a chosen clone back to origin.
 * 
 * There are two kinds of candidates for chosen clone: 
 * 
 * - about to commit within callback
 * - returned from callback
 * 
 * After a candidate commits, it's the chosen one before returning. But,
 * instead of choosing immediately as a race condition, the runtime may 
 * pause operation to choose a candidate randomly or heuristically. The
 * runtime shall disfavor candidates that returned in an error state. 
 * 
 * After a choice is made, other clones are uncreated and aborted. The
 * uncreated error informs the callback function to abandon its efforts.
 */
void glas_cx_choice(glas* origin, size_t count, void* cbarg, 
    void (*callback)(glas* clone, size_t index, void* cbarg));

/***************************
 * QUICK START
 ***************************/

/**
 * The default initializer.
 * 
 * This loads primitives to "%" and a user configuration from the OS
 * environment into "conf.". Binds "%env." to "conf.env.".
 * 
 * Configuration is sought in:
 * 
 * - GLAS_CONF environment variable     if defined
 * - ${HOME}/.config/glas/conf.glas     on Linux
 * - %AppData%\glas\conf.glas           on Windows (eventually)
 * 
 * See `glas_load_config` for more details there.
 * 
 * This is a utility function, intended to serve as the main starting
 * point for clients of this API, but a client could implement it using
 * the API and a few system APIs.
 */
void glas_init_basic(glas*);

/***************************
 * MEMORY MANAGEMENT
 **************************/

/**
 * Reference counting shared objects.
 * 
 * Zero-copy binaries and foreign pointers are reference-counted to
 * extend garbage collection beyond the runtime boundary. The count 
 * itself is abstracted.
 * 
 * All runtime APIs assume references are pre-incremented. Thus, to
 * release the object, the recipient needs only to decref. Unmanaged
 * objects may set refct_upd to NULL.
 */
typedef struct {
    void (*refct_upd)(void* refct_obj, bool incref);
    void  *refct_obj;
} glas_refct;

inline void glas_decref(glas_refct c) {
    if(NULL != c.refct_upd) {
        c.refct_upd(c.refct_obj, false);
    }
}

inline void glas_incref(glas_refct c) {
    if(NULL != c.refct_upd) {
        c.refct_upd(c.refct_obj, true);
    }
}

/************************
 * NAMESPACES
 ************************/

/**
 * Test for definitions.
 * 
 * The has_prefix variant returns true if at least one name with the
 * prefix is definied.
 * 
 * In the general case, this test may involve loading and compiling
 * files due to lazy evaluation of bound prefixes.
 */
bool glas_ns_has_def(glas*, char const* name);
bool glas_ns_uses_prefix(glas*, char const* prefix);

/**
 * Define a name.
 * 
 * A namespace object is popped from the stack and assigned a name. All 
 * namespace objects are supported, not just programs definitions. The 
 * context namespace is lexical, thus the definition is visible only to
 * future operations in the context.
 */
void glas_ns_define(glas*, char const* name);

/**
 * Define many names.
 * 
 * A subset of namespace objects represent environments, logically a
 * dictionary of names `{ x = def_of_x, y = def_of_y, ... }`.
 * 
 * This function pops an environment from the data stack and binds it 
 * to a specified prefix. For example, if prefix is "foo." we'd define
 * "foo.x" and "foo.y". This use of dotted paths is a convention.
 * 
 * Binding preserves lazy evaluation of the environment. Thus, there may
 * be a lot of extra processing when first requesting a name. Consider 
 * use of `glas_call_prepare` to begin processing in the background.
 */
void glas_ns_bindenv(glas*, char const* prefix);

/**
 * Push copy of definition onto stack.
 * 
 * If the name is undefined, this will result in an error when trying
 * to call it (due to lazy evaluation). 
 */
void glas_ns_pushdef(glas*, char const* name);

/**
 * Push full or partial copy of context namespace onto stack.
 *  
 * For symmetry with define, pushdef, and bind, includes a prefix. For
 * prefix "foo.", environment includes `{ x = foo.x, y = foo.y }` etc..
 * Use prefix "" for full namespace, NULL for empty namespace.
 */
void glas_ns_pushenv(glas*, char const* prefix);


/**
 * Evaluate namespace AST representation to namespace object.
 * 
 * Clients can construct an AST representation on the data stack, then
 * evaluate to a namespace object. This AST is evaluated in an empty 
 * environment, thus usually represents a function `Env -> AST'`. Error
 * if the AST is non-linear.
 * 
 * See GlasNamespaces.md for details.
 * 
 * Notes:
 * 
 * - Most namespace operations can be defined using eval and apply.
 *   Excepted are references with identity: callbacks, registers, etc.
 * - All namespace objects are sealed by the runtime. Clients cannot
 *   view the evaluated representation (modulo reflection APIs).
 */
void glas_ns_eval(glas*);

/**
 * Apply a namespace function.
 * 
 * Pops namespace function then namespace argument from stack; pushes a
 * thunk representing a future result of applying function to argument. 
 * 
 * Namespace thunks are evaluated lazily by default, but may be forced
 * or sparked. A consequence of lazy evaluation.
 */
void glas_ns_apply(glas*);

/**
 * Utility for data definitions.
 * 
 * Wraps data into an AST (with data tag), evaluates, defines name.
 * Error if the data is linear.
 */
void glas_ns_defdata(glas*, char const* name);

/**
 * Utility for tagging definitions.
 * 
 * By convention, all definitions in glas systems should be tagged, e.g. 
 * with "prog" for `Env -> Program` definitions (Env being the caller's 
 * environment) or "data" for embedded data definitions. Tags serve as 
 * calling conventions and adapter hooks.
 * 
 * This API supports tagging and removing tags from namespace object at
 * top of data stack. This could be implemented via AST eval and apply.
 */
void glas_ns_tag(glas*, char const* tag);
void glas_ns_untag(glas*, char const* tag);

/**
 * Prefix-to-prefix Translations.
 * 
 * The glas namespace model uses prefix-to-prefix translations for many
 * purposes. Within C, we'll simply represent these as an array of pairs
 * of strings. 
 * 
 *   { {"foo.", "bar."}, { "$", NULL }, { NULL, NULL } }
 * 
 * In each case, lhs represents a match prefix, the rhs the update. The
 * rhs may be NULL, indicating the matched prefix links to nothing. Only 
 * the longest matching prefix is applied to each name, if any.
 * 
 * Note: To support translations, a ".." suffix is logically added to
 * every name. Leveraging this, we can translate "bar.." to "foo.." or
 * "bar." to "foo." (affecting 'bar.*'), without accidentally converting
 * "bard" to "food".
 */
typedef struct { char const *lhs, *rhs; } glas_tl; 

/**
 * Construct an environment translation function.
 * 
 * This adds a namespace function to the stack that, when applied to
 * an namespace environment, translates access to the names within.
 */
void glas_ns_pushtl(glas*, glas_tl const*);

/**
 * Apply any `Env -> Env` namespace op to context namespace.
 * 
 * For example, apply a translation from pushtl, or apply an ad hoc
 * mixin. May apply to a specific prefix. This can be implemented via
 * `pushenv swap apply bindenv`, taking the operation from the stack.
 */
void glas_ns_include(glas*, char const* target_prefix);

/**
 * Define programs using callbacks.
 * 
 * Each invocation receives a 'glas*' callback context, valid for the
 * duration of the callback and implicitly dropped upon return. 
 * 
 * This context provides access to the caller's environment through a
 * specified prefix (e.g. "$"), and a host environment through another
 * prefix (e.g. ""). The former supports algebraic effects handlers and
 * registers as pass-by-ref args. The latter supports lexical closure.
 * Either or both may be NULL, inaccessible. 
 * 
 * Caller prefix will shadow host when there is overlap. 
 * 
 * To simplify the API, callbacks are marked as atomic or not:
 * 
 * - An atomic callback cannot commit steps. Use on_commit to defer.
 *   The operation may abort and retry locally.
 * 
 * - A non-atomic callback may commit steps, but cannot be called from
 *   %atomic sections within a program.
 * 
 * Like forks, a callback context may be uncreated if its caller aborts.
 * 
 * The glas system distinguishes errors and failures. Callbacks report
 * failure by returning false, and error by adding flags to the context.
 * 
 * In general, callback functions must be multi-threading safe.
 */
typedef struct {
    bool (*operation)(glas*, void* cbarg);
    void* cbarg;                // opaque, passed to operation
    glas_refct refct;           // memory management for cbarg
    char const* caller_prefix;  // e.g. "$"
    char const* host_prefix;    // e.g. "" ("$" shadowed by client)
    uint8_t ar_in, ar_out;      // data stack arity 
    bool atomic;                // no commit/abort, more widely usable
} glas_prog_cb;

/**
 * Define by callback.
 * 
 * This is a utility function that can be defined in terms of pushenv,
 * pushcb, apply, and define. But it covers the most common use case,
 * where we want to bind host_prefix to the context namespace at time
 * of definition (lexical closure). 
 */
void glas_ns_defcb(glas*, char const* name, glas_prog_cb const*);

/**
 * Push anonymous callback as namespace object to stack.
 * 
 * If host_prefix is NULL, this represents a ProgDef. Otherwise, it
 * represents `Env -> ProgDef` and requires an additional parameter
 * for the host closure.
 */
void glas_ns_pushcb(glas*, glas_prog_cb const*);

/****************************
 * LOAD DEFINITIONS IN BULK
 ***************************/

/**
 * Load primitive definitions.
 * 
 * This pushes an abstract namespace environment onto the data stack
 * that contains all the primitives. The client could use bindenv on
 * prefix "%" to integrate them.
 * 
 * This doesn't include names managed by front-end compilers, such as
 * %src, %env, %arg, and %self. However, it does include annotations,
 * accelerators, etc. not just program-model primitives.
 */
void glas_load_prims(glas*);

/**
 * TBD: override macro file loader, or perhaps virtualize filesystem.
 * 
 * Enable client to provide 'sources' without the full filesystem.
 */

/**
 * Load a configuration.
 * 
 * This operation loads a configuration file:
 * 
 * - apply built-in compiler to file
 * - fixpoint bindings of %self and %env
 * - provide an %arg for runtime version info 
 * - bootstrap of %env.lang.glas and %env.lang.glob
 * 
 * It's difficult to tease this knot apart, and it's convenient to
 * have it as one big op.
 */
void glas_load_config(glas*, char const* file);

/**
 * Load a script file.
 * 
 */

/**
 * Pushes a namespace object onto the stack that represents the 
 * glas built-in compiler for a given file extension (if any).
 * 
 * Fails, returning false, if no such compiler exists.
 */
bool glas_load_builtin_compiler(glas*, char const* fileExt);


/**************************
 * CALLING FUNCTIONS
 **************************/

/**
 * Run a defined program.
 * 
 * The called program receives access to the caller's data stack and 
 * namespace. We can optionally translate the namespace. NULL for the
 * glas_tl pointer indicates there is no translation. NULL for name
 * indicates popping anonymous definition from stack.
 * 
 * The `_atomic` variant implicitly wraps the program with '%atomic'. 
 * This is useful when we want to ensure a program does not commit a
 * step. But it does restrict some operations, e.g. only the callbacks
 * marked 'atomic' are permitted.
 * 
 * Only two kinds of definitions are supported at this time:
 * - tag "prog": Env -> Program.
 * - tag "data": just embedded data
 */
void glas_call(glas*, char const* name, glas_tl const*);
void glas_call_atomic(glas*, char const* name, glas_tl const*);

/**
 * Ask runtime to prepare definitions.
 * 
 * Namespaces in glas systems are lazily evaluated. Binding to a prefix, 
 * especially. But we may indicate which names we'll need in advance to
 * let the runtime begin working in advance.
 */
void glas_call_prep(glas*, char const* name);

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
 * This always rewinds context to the last committed step. If retried,
 * aborted steps may have different outcomes due to non-deterministic
 * choice and external state changes.
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
 * Every register has a logically associated opqueue via abstract data
 * environments (see `glas_reg_assoc`). But in practice, you'll likely
 * want global registers for client resources not tied to a context. 
 */
void glas_step_on_commit(glas*, void (*op)(void* arg), void* arg, 
    char const* opqueue);

/**
 * Defer operations until abort.
 * 
 * This is useful to clean up memory allocated for on_commit, but it
 * may find other use cases.
 */
void glas_step_on_abort(glas*, void (*op)(void* arg), void* arg);
void glas_step_on_abort_decref(glas*, glas_refct);

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
 * step, instead of always retrying the full step. Callback contexts 
 * already support this to a degree, aborting to start of callback 
 * instead of to caller's last step. But it's really awkward to use 
 * from C for checkpoints.
 * 
 * Not a high priority, at the moment.
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
 */
typedef enum GLAS_ERROR_FLAGS {
    GLAS_NO_ERRORS          = 0x000000,   

    // EXTERNAL ERRORS
    GLAS_E_CONFLICT         = 0x000001, // concurrency conflicts; retry might avoid
    GLAS_E_UNCREATED        = 0x000002, // an aborted fork or callback context
    GLAS_E_QUOTA            = 0x000004, // client-requested quota or timeout
    GLAS_E_CLIENT           = 0x000008, // client-inserted error

    // OPERATION ERRORS
    GLAS_E_ERROR_OP         = 0x001000, // explicit divergence in program
    GLAS_E_LINEARITY        = 0x002000, // copy or drop of linear data
    GLAS_E_DATA_SEALED      = 0x004000, // failed to unseal data before use
    GLAS_E_NAME_UNDEF       = 0x008000, // attempt to use an undefined name
    GLAS_E_EPHEMERALITY     = 0x010000, // ephemeral data, persistent register
    GLAS_E_ATOMICITY        = 0x020000, // attempted commit in atomic callback

    // In general: I would like to restrict error flags to things a C 
    // program makes meaningful decisions about. Details in error logs.

} GLAS_ERROR_FLAGS;

GLAS_ERROR_FLAGS glas_errors_read(glas*);         // read error flags
void glas_errors_write(glas*, GLAS_ERROR_FLAGS);   // bitwise 'or' to error flags (monotonic)

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
void glas_data_drop(glas*, uint8_t amt); // S.. B C -- S.. ; drop 2
void glas_data_swap(glas*); // A B -- B A

/**
 * Move data to or from an auxilliary stack, called the stash.
 * 
 * If amt > 0, moves data to the stash. If amt < 0, transfers |amt| 
 * from stash to data stack. Equivalent to moving one item at a time.
 * 
 * Although callback contexts access part of the caller's stack, they
 * each have their own stash. Anything left on the stash at end of a
 * callback is dropped (which may result in linearity errors).
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
void glas_data_move(glas*, char const* moves);

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
void glas_binary_push_zc(glas*, uint8_t const*, size_t len, glas_refct);

/**
 * Non-destructively read binary data from top of data stack.
 * 
 * The base version will copy the binary. The zero-copy (_zc) variant
 * may need to flatten part of a rope-structured binary, then returns
 * a reference. The client must not modify the latter memory, and must 
 * decref the reference count when done.
 * 
 * Flattening a binary involves a copy. However, the zero-copy variant
 * is useful if the data was already flattened, would be in the future,
 * or will be requested many times.
 * 
 * The peek operation returns 'true' if end-of-list was reached and
 * everything available was read. The operation will return false with 
 * a partial result if data is only partially a valid binary. Peek does
 * not cause a runtime error even for invalid data.
 * 
 * Note: If buf/ppBuf is NULL, the runtime still attempts to produce
 * valid results for amt_read and return value, and flatten for _zc.
 */
bool glas_binary_peek(glas*, size_t start_offset, size_t max_read, 
    uint8_t* buf, size_t* amt_read);
bool glas_binary_peek_zc(glas*, size_t start_offset, size_t max_read,
    uint8_t const** ppBuf, size_t* amt_read, glas_refct*);

/**
 * Push and peek bitstrings as binaries.
 * 
 * To support bitstrings that aren't an exact multiple of 8 bits, we
 * have a variant for a partial first byte (_pfb). This is encoded as:
 * 
 *      msb  lsb   bits
 *      10000000    0
 *      a1000000    1
 *      ab100000    2
 *      abcdefg1    7
 * 
 * There are no zero-copy variants. Bitstrings are compactly encoded,
 * but not as binaries. The runtime assumes most bitstrings will be
 * short, e.g. integers or keys for a radix-tree dict.
 */
void glas_bitstr_push(glas*, uint8_t const* buf, size_t buflen);
bool glas_bitstr_peek(glas*, size_t octet_offset, size_t max_read, 
    uint8_t* buf, size_t* amt_read);
void glas_bitstr_push_pfb(glas*, uint8_t const* buf, size_t buflen);
bool glas_bitstr_peek_pfb(glas*, size_t octet_offset, size_t max_read, 
    uint8_t* buf, size_t* amt_read);

/**
 * Push and peek for integers.
 * 
 * Integers are represented by variable-width bitstrings, msb to lsb, 
 * with negatives as the ones' complement (invert the bits):
 * 
 *      Int       Bits
 *       42     101010
 *       12       1100
 *        7        111
 *        0              (empty)
 *       -7        000
 *      -12       0011
 *      -42     010101
 * 
 * These integer push/peek operators perform the conversion to and from
 * the C representations of integers. A peek operation may fail if the
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
 * The client can push and peek pointers, and use refct for memory 
 * management. Useful for callback-based APIs.
 */
void glas_ptr_push(void*, glas_refct);
bool glas_ptr_peek(void**, glas_refct*);


/*****************************************
 * REGISTERS
 ****************************************/

/**
 * New registers.
 * 
 * Introduces a new environment of registers as a namespace object on 
 * the data stack. All names are defined, each a unique register, all
 * initialized to zero. Logically, at least. In practice, the registers
 * are lazily created and may be garbage-collected if they hold a zero.
 * 
 * Aside: I call these 'registers' in context of glas programs, where
 * they are second-class - i.e. no pointers to registers. However, to
 * the client, registers and entire volumes thereof are first-class.
 */
void glas_load_reg_new(glas*);

/**
 * Associative registers.
 * 
 * Introduces a space of registers identified by a directed edge between
 * two other registers. The same two register arguments will always find
 * the same space, thus this isn't necessarily unused. 
 * 
 * Primary use case is abstract data environments. Instead of sealing
 * data, we can hide registers by controlling access to other registers.
 */
void glas_load_reg_assoc(glas*, char const* r1, char const* r2);

/**
 * Runtime-global registers.
 * 
 * Introduces a singleton environment of registers shared between all 
 * contexts, i.e. bound to static globals in the library.
 */
void glas_load_reg_global(glas*);

/**
 * TBD: Persistent registers.
 * 
 * Bound to a database file, perhaps.
 */


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
 * More specialized registers: bags, crdts, dict reg as kvdb, etc..
 */


/****************
 * DATA SEALING
 ****************/

/**
 * Protect data from tampering.
 * 
 * Seal and unseal reference a register as the 'key' for the data.
 */
void glas_data_seal(glas*, char const* key);
void glas_data_unseal(glas*, char const* key);


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


/*******************************
 * NAMESPACES AND DEFINITIONS
 *******************************/

/** TBD
 * - access to bgcalls
 * - access to other reflection APIs
 *   - logging
 *   - error info
 *   - etc.
 */


#define GLAS_H
#endif


