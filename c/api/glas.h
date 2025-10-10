/**
 * Use glas runtime as a library.
 * 
 * On `glas_thread_new`, the client (your program) receives a `glas*`
 * context. This represents a remote-controlled glas coroutine that 
 * begins with an empty namespace, stack, and auxilliary stash.
 * 
 * Information exchange between client and runtime is restricted to 
 * binaries, bitstrings, and client pointers (as abstract data). The
 * stack may contain wider varieties of data, including definitions
 * or reified namespace environments.
 * 
 * Error handling is transactional: the client performs a sequence of
 * operations on a thread then commits the step. In case of error or
 * conflict, the step fails to commit. But the client can rewind and 
 * retry, or try something new. 
 * 
 * Because C is not transactional, on_commit and on_abort callbacks 
 * exist to simplify integration.
 * 
 * Note: Names in glas cannot contain NULL values, which makes them
 * generally compatible with C strings.
 */
#pragma once
#ifndef GLAS_H
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

/*******************************************
 * GLAS THREAD AND CONTEXT
 ******************************************/
/**
 * Reference to a glas thread.
 * 
 * The glas thread is the primary context for the glas runtime. It has
 * a stack, stash, and namespace, plus bookkeeping for transactions,
 * checkpoints, and errors. The thread awaits commands from the client.
 * 
 * Individual glas threads are not mt-safe, but operations on separate
 * threads is mt-safe. The glas runtime does not use thread-local state,
 * thus glas threads may freely migrate between OS threads. 
 */
typedef struct glas glas;

/**
 * Create a new glas thread.
 * 
 * Starts with empty namespace, stack, and stash. The recommended next
 * step is `glas_init_default`. 
 */
glas* glas_thread_new();

/**
 * Terminate a glas thread.
 * 
 * This tells the runtime that the client is done with this thread,
 * i.e. that no further client commands are forthcoming. Any pending
 * operations are aborted, then associated resources are recycled.
 */
void glas_thread_exit(glas*);

/**
 * Forking threads.
 * 
 * A glas thread may create another glas thread, sharing context. I will
 * distinguish origin (the argument) and fork (return value). 
 * 
 * Fork receives a copy of origin's namespace, and an optional transfer 
 * from origin's data stack. After construction, runtime interaction is 
 * only through shared registers, i.e. no further stack transfers. 
 * 
 * Fork is not fully stable after returning from `glas_thread_fork`. It
 * is possible origin aborts the step, in which case fork is 'uncreated' 
 * and will never commit. Best practice is to defer operations on fork
 * until after commit.
 */
glas* glas_thread_fork(glas* origin, uint8_t stack_transfer);

/**
 * Set debug name for a thread.
 * 
 * To appear in warning messages, stack traces, etc..
 */
void glas_thread_set_debug_name(glas*, char const* debug_name);

/**
 * Concurrent search of non-deterministic choice.
 * 
 * A glas thread can awkwardly search non-deterministic choices, but we
 * can do a lot better with a little guidance and multi-threading. 
 * 
 * I'll distinguish origin (argument to choice) and clone (argument to 
 * client callback). The runtime creates up to N clones, assigning each
 * an index in 0..(N-1). A clone receives a copy of origin's state, and
 * is evaluated within the callback.
 * 
 * The final state of a chosen clone is transferred back to origin. The
 * candidates for chosen one:
 * 
 * - any clone that is about to commit successfully
 * - any clone that has returned from the callback
 * 
 * After a candidate is chosen, all running clones are uncreated, which
 * serves as a signal to abandon efforts. All unchosen, returned clones
 * are aborted. And pending clones won't ever be created. After commit,
 * a chosen clone continues running until return from callback.
 * 
 * Non-deterministic choice doesn't imply random choice. The runtime may 
 * apply heuristics when choosing a candidate. The simplest heuristic is
 * a race condition: first candidate wins. However, the runtime should 
 * disfavor clones that return swiftly with an error state.
 * 
 * The runtime is expected to evaluate clones concurrently using worker
 * threads, though how many may depend on resource constraints.
 */
void glas_choice(glas* origin, size_t N, void* cbarg, 
    void (*callback)(glas* clone, size_t index, void* cbarg));


/**
 * The default initializer.
 * 
 * A user configuration is sought in:
 * 
 * - GLAS_CONF environment variable     if defined
 * - ${HOME}/.config/glas/conf.glas     on Linux
 * - %AppData%\glas\conf.glas           on Windows (eventually)
 * 
 * This initializer binds primitives to "%", and the compiled user 
 * configuration to "conf.". Binds "%env." to final "conf.env.". Also
 * supports bootstrap of %env.lang.glas and %env.lang.glob if feasible.
 * 
 * This sets the client up for most use cases for glas systems. We'll
 * add further steps for loading scripts and running applications.
 */
void glas_init_default(glas*);

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
 * A subset of namespace objects represent environments, basically a
 * dictionary of names `{ x = x_def, y = y_def, ... }` but with lazy
 * evaluation.
 * 
 * This function pops an environment from the data stack and binds it 
 * to a specified prefix. For example, if prefix is "$" we'd define "$x" 
 * and "$y". For dotted paths, we might use "foo." as a prefix.
 * 
 * All previous names with the same prefix are no longer in scope, even
 * when they aren't defined in the bound environment. For example, we 
 * can nuke a thread's namespace by binding an empty environment to "".
 * 
 * Binding a name to a prefix is semantically distinct from defining a
 * name to hold an environment. The two can serve similar roles under
 * assumptions about front-end syntax. In glas systems, the convention
 * is to build a big, flat namespace with hierarchy in names, e.g. the
 * "." in "foo.x" is part of the name, not syntax for env access. This
 * structure simplifies translations and overrides.
 * 
 * Binding preserves lazy evaluation of an environment. Thus, there may
 * be extra processing when first requesting a name. Consider use of
 * `glas_call_prep` to load definitions in a worker thread.
 */
void glas_ns_bindenv(glas*, char const* prefix);

/**
 * Push copy of definition onto stack.
 * 
 * The definition is lazy, thus no error if name is undefined. Not until
 * called, at least. Also, all namespace objects are abstract. You can
 * only peek inside via reflection APIs.
 */
void glas_ns_pushdef(glas*, char const* name);

/**
 * Push full or partial copy of the thread's namespace onto stack.
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
void glas_ns_ast_eval(glas*);

/**
 * Apply a namespace function.
 * 
 * Pops namespace function then namespace argument from stack; pushes a
 * thunk representing a future result of applying function to argument. 
 * 
 * Namespace thunks are evaluated lazily by default, but may be forced
 * or sparked. A consequence of lazy evaluation.
 */
void glas_ns_op_apply(glas*);

/**
 * Define name to embedded data.
 * 
 * This wraps arbitrary data into a namespace object for use as a
 * definition, then promptly binds to a name.
 */
void glas_ns_defdata(glas*, char const* name);

/**
 * Extract a definition from Env on stack.
 * 
 * Pops Env from stack, pushes definition of name from that Env. Lazy,
 * thus no error if name is undefined.
 */
void glas_ns_env_extract(glas*, char const* name);

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
 * Construct an environment translation op.
 * 
 * The resulting namespace object on the stack represents a function 
 * that, when applied, renames things in the environment. Use lhs for
 * the new names, rhs for the current names. 
 */
void glas_ns_pushtl(glas*, glas_tl const*);

/**
 * Apply any `Env -> Env` namespace op to current namespace.
 * 
 * For example, apply a translation from pushtl, or apply an ad hoc
 * mixin. May apply to a specific prefix. This can be implemented via
 * `pushenv swap apply bindenv`, taking the operation from the stack.
 */
void glas_ns_include(glas*, char const* target_prefix);


/**
 * Define programs using callbacks.
 * 
 * A callback receives namespace bindings for host and caller. The host
 * represents lexical closure where the callback was defined. The caller
 * supports pass-by-ref registers and algebraic-effects handlers. 
 * 
 * The callback also receives limited access to the caller's data stack 
 * and an empty stash. Access to the data stack is controlled by input 
 * arity. If output arity is not respected, we'll kill the thread.
 * 
 * Step commit is handled in a special way by callbacks:
 * 
 * - If the callback never commits, the operation is 'atomic' and commit
 *   is controlled by the caller.
 * - If the callback does commit, then any pending operations after the
 *   final commit are aborted. The runtime may warn if non-trivial.
 * - Attempts to commit in atomic sections cause atomicity errors. The
 *   client may specify no_atomic to support analysis prior to calls. 
 * 
 * An uncreated error is possible prior to first commit, indicating the
 * call itself was aborted due to non-deterministic choice or conflict.
 * 
 * Forked of threads is also handled carefully: the caller waits for all
 * forks to terminate before continuing, i.e. fork-join semantics. This
 * is because caller environment is often invalid after return.
 * 
 * Callback operations may be called from multiple threads concurrently,
 * in general, thus should be mt-safe, in general.
 */
typedef struct {
    char const* debug_name;
    bool (*operation)(glas*, void* cbarg);
    void* cbarg;                // opaque, passed to operation
    glas_refct refct;           // memory management for cbarg
    char const* caller_prefix;  // e.g. "$"
    char const* host_prefix;    // e.g. "" (caller shadows "$")
    uint8_t ar_in, ar_out;      // data stack arity (enforced!)
    bool no_atomic;             // forbid calls in atomic sections
} glas_prog_cb;

/**
 * Define by callback.
 * 
 * This is a utility function that can be defined in terms of pushenv,
 * pushcb, apply, and define. But it covers the most common use case,
 * where we want to bind host_prefix to the thread namespace at time
 * of definition (lexical closure). 
 */
void glas_ns_defcb(glas*, char const* name, glas_prog_cb const*);

/**
 * Push anonymous callback as namespace object to stack.
 * 
 * If host_prefix is NULL, this represents a ProgDef. Otherwise, it
 * represents `Env -> ProgDef` and must be applied to an Env for the
 * host closure.
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
 * TBD: Load a file. 
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
 * locked down. Instead, clients keep transactions small, avoid data
 * contention, and retry as needed. 
 * 
 * The C language does not make 'undo' easy. To mitigate, the runtime
 * provides 'on_commit' and 'on_abort' operations. Clients thus defer
 * actions that are difficult to undo, or prepare operations that are
 * easy to undo.
 */
bool glas_step_commit(glas*);

/**
 * Abort the current step. 
 * 
 * This rewinds a thread's state to the last committed step. The client
 * will often wish to rewind their own state, especially allocations. 
 * This can be supported via `glas_step_on_abort`. 
 */
void glas_step_abort(glas*);

/**
 * Committed action.
 * 
 * For operations that are difficult to undo, clients may defer action 
 * until after commit. This prevents observation of results within the
 * step, but if that's acceptable it greatly simplifies integration.
 * 
 * To maintain transactional ordering of operations, operations can be
 * organized into queues that are written within the transaction. These
 * queues are then processed sequentially by runtime worker threads. The
 * NULL queue is an exception, processed before return from step_commit.
 * 
 * Operations queues are associated with registers: the client names a
 * register for unique identity, but that register is unmodified. Use of
 * global registers is recommended in this role.
 * 
 */
void glas_step_on_commit(glas*, void (*op)(void* arg), void* arg, 
    char const* opqueue);

/**
 * Defer operations until abort.
 * 
 * This is mostly useful to clean up allocated memory, but it may find
 * other use cases. These are run before return from `glas_step_abort`.
 * Order is reversed: last inserted is first executed.
 */
void glas_step_on_abort(glas*, void (*op)(void* arg), void* arg);
void glas_step_on_abort_decref(glas*, glas_refct);

/**
 * Checkpoints.
 * 
 * Each glas thread has a stack of checkpoints, ordered monotonically: 
 * top of stack is always the most recent checkpoint. At the start of
 * each step, this stack contains one checkpoint, equivalent to abort.
 * 
 * During the step, the program may save or push new checkpoints. Save
 * will replace the most recent checkpoint. Push will add a new one to
 * the checkpoint stack. Save and push may fail for the same reasons as
 * commit may fail: conflict and errors. 
 * 
 * Loading a checkpoint rewinds context state to the moment immediately
 * before that checkpoint was saved or pushed. Thus, in case of retry, a
 * client must again save or push the checkpoint and check for failure.
 * Load will also process 'on_abort' operations corresponding to partial
 * rollback.
 * 
 * Checkpoints are relatively cheap, but they may encourage long-running
 * transactions that run greater risk of conflict. 
 */
bool glas_checkpoint_save(glas*); // overwrite last checkpoint on success
bool glas_checkpoint_push(glas*); // push new checkpoint onto stack on success
void glas_checkpoint_drop(glas*); // drop last pushed checkpoint
void glas_checkpoint_load(glas*); // update state to recent checkpoint


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
    GLAS_E_UNRECOVERABLE    = 0x000001, // abort won't fix unrecoverable errors

    GLAS_E_CONFLICT         = 0x000002, // concurrency conflicts; retry might avoid
    GLAS_E_UNCREATED        = 0x000005, // an aborted fork or callback context
    GLAS_E_QUOTA            = 0x000008, // client-requested quota or timeout
    GLAS_E_CLIENT           = 0x000010, // client-inserted error

    // OPERATION ERRORS
    GLAS_E_ERROR_OP         = 0x001000, // explicit divergence in program
    GLAS_E_LINEARITY        = 0x002000, // copy or drop of linear data
    GLAS_E_DATA_SEALED      = 0x004000, // failed to unseal data before use
    GLAS_E_NAME_UNDEF       = 0x008000, // attempt to use an undefined name
    GLAS_E_EPHEMERALITY     = 0x010000, // ephemeral data in persistent register
    GLAS_E_ATOMICITY        = 0x020000, // attempted commit in atomic callback
    GLAS_E_ASSERT           = 0x040000, // runtime assertion failures
    GLAS_E_DATA_TYPE        = 0x080000, // runtime type errors
    GLAS_E_DATA_QTY         = 0x100000, // e.g. for queue reads
    GLAS_E_UNDERFLOW        = 0x200000, // stack or stash underflow
} GLAS_ERROR_FLAGS;

GLAS_ERROR_FLAGS glas_errors_read(glas*);           // read error flags
void glas_errors_write(glas*, GLAS_ERROR_FLAGS);    // monotonic via bitwise 'or'


/***************************************
 * DATA STACK MANIPULATION
 ***************************************/

 /**
  * Basic Stack Manipulations.
  * 
  * The glas program model has a few simple operations: %copy, %drop, 
  * %swap, and %dip. But this assumes a compiler will eliminate these
  * operations, reducing the stack to logical locations. For this API,
  * we'll provide a few bulk ops.
  */
void glas_data_copy(glas*, uint8_t amt); // A B -- A B A B ; copy 2
void glas_data_drop(glas*, uint8_t amt); // A B C -- A ; drop 2
void glas_data_swap(glas*);              // A B -- B A

/**
 * Transfer data to or from auxilliary stack, called stash.
 * 
 * If amt > 0, transfers amt items from stack to stash. If amt < 0, 
 * transfers |amt| items from stash to stack. Always equivalent to 
 * transferring one item at a time (modulo performance).
 * 
 * The stash serves a similar role as the program primitive %dip, hiding
 * part of the stack from a subprogram. Callback contexts do not receive 
 * access to the caller's stash, and items in the callback context stash
 * are orphaned upon return.
 */
void glas_data_stash(glas*, int8_t amt);

/**
 * Visualize data stack shuffling with a C text literal.
 *   
 *   "abc-abcabc"   copy 3
 *   "abc-b"        drops a and c
 *   "abcd-abcab"   drops d, copies ab to top of stack
 * 
 * This operation navigates to '-', scans leftwards, popping items into
 * local variables [a-zA-Z]. Then it scans rightwards from '-', pushing 
 * variables back onto the stack. It's an error if the string reuses a
 * variable on the left, refers to unassigned variables on the right, or
 * lacks the '-' separator. Copy or drop of linear data is detected.
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
 * The runtime assumes the client does not modify a zero-copy binary.
 * 
 * The runtime logically treats binaries as a list of small integers, 
 * but the representation is heavily optimized.
 * 
 * Note: Texts are encoded as utf-8 binaries in glas systems. There are
 * also a few operations to translate binaries to bitstrings and back.
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
 * 
 * For larger bitstrings, try binary conversions or dict operations.
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
 * Pointers - abstract client data
 * 
 * The runtime treats pointers as an abstract, runtime-ephemeral data
 * type. Pointers aren't linear, but I encourage clients to seal most
 * pointers with `glas_data_seal_linear` unless there is a good reason
 * to not do so.
 * 
 * Peek will fail, returning false, if either argument is NULL or if 
 * the top stack element is not a pointer.
 */
void glas_ptr_push(void*, glas_refct);
bool glas_ptr_peek(void**, glas_refct*);


/****************
 * DATA SEALING
 ****************/

/**
 * A dynamic approach to abstract data types.
 * 
 * When sealed, a key is referenced in the thread namespace. Currently,
 * the key must name a register, but there is opportunity for extension.
 * This data cannot be accessed until unsealed by the same key. Attempts
 * to do so result in errors.
 * 
 * In glas systems, convention is to favor linear objects and abstract
 * data environments (via `glas_reg_assoc`) instead of abstract data 
 * types for references. But seals remain useful for other roles.
 */
void glas_data_seal(glas*, char const* key);
void glas_data_unseal(glas*, char const* key);

/** 
 * A dynamic approach to abstract, linear data objects.
 * 
 * The _linear variants seal data as above, but also forbid the sealed
 * data from being copied or dropped, raising linearity errors. Linear 
 * seal must be matched by linear unseal.
 * 
 * Linearity is useful for enforcing protocols, such as ensuring files 
 * are closed after opening.
 * 
 * Although linearity forbids logical copying, this doesn't imply that 
 * there is only one reference to the object. Transactions will hold a
 * copy of data for undo. Non-deterministic is subject to concurrent
 * evaluation (see `glas_cx_choice`), thus a cient might see the same
 * linear data or pointer from many threads.
 * 
 * Linearity also isn't bullet-proof against drops. Although an error to
 * drop linear data directly, it isn't an error to close the thread that
 * holds linear data on its stack. At most, the runtime reports warnings 
 * when linear data is orphaned.
 * 
 * Despite these caveats, linearity remains useful.
 */
void glas_data_seal_linear(glas*, char const* key);
void glas_data_unseal_linear(glas*, char const* key);

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
 * they are second-class and statically bounded. However, the client is
 * free to treat environments of registers as mutable dictionaries.
 */
void glas_load_reg_new(glas*);

/**
 * Associative registers.
 * 
 * Introduces a unique space of registers identified by an ordered pair 
 * of registers. The same registers will always find the same space.
 * 
 * Primary use case is abstract data environments. Instead of sealing
 * data, we can hide registers by controlling access to other registers.
 */
void glas_load_reg_assoc(glas*, char const* r1, char const* r2);

/**
 * Runtime-global registers.
 * 
 * Shared registers bound to static globals in the runtime library. This
 * allows for sharing data even between independent threads, or building
 * something like a shared databus or pubsub model.
 * 
 * Global registers serve as a useful proxy for client resources, such
 * as for operations queues in `glas_step_on_commit`.
 */
void glas_load_reg_global(glas*);

/**
 * TBD: Persistent registers.
 * 
 * We should eventually bind some registers to external databases to
 * support orthogonal persistence. However, loading more than one db
 * per transaction have very limited support. Also, we might need to
 * somehow handle data sealed under a different db's registers, e.g.
 * via encryption.
 */

/**
 * Swap (read-write) data between data stack and named register.
 * 
 * This is the only primitive operation on registers: the linear swap. 
 * In practice, I expect clients will favor get, set, and queue ops.
 */
void glas_reg_rw(glas*, char const*); // A -- B

/**
 * The ever popular get/set operations.
 * 
 * The get operation will copy data from a cell to the stack. The set 
 * operation pops data from the stack and overwrites the register. These
 * are the primitive mutable variable operations in most languages.
 * 
 * In glas, they are not primitive because they conflict with linearity.
 * But they are still very useful for precise conflict analysis. 
 * 
 * A transaction can often operate on a read-only snapshots of state. A
 * write-only transaction cannot conflict with any other transaction. By
 * operating in terms of get and set, optimistic concurrency will have 
 * fewer read-write conflicts.
 */
void glas_reg_get(glas*, char const*); // -- A
void glas_reg_set(glas*, char const*); // A --

/**
 * Treat a register as a queue.
 * 
 * The register must contain a list, and is used under constraints: the
 * reader cannot perform partial reads, i.e. error if fewer items than 
 * requested are available. A writer must not read register contents.
 * 
 * Under these constraints, we can support a single reader and many
 * concurrent writers without a read-write conflict. Each writer can
 * implicitly buffer writes during the transaction, then apply them all
 * at once upon commit, preserving transaction isolation.
 */
void glas_reg_queue_read(glas*, char const*); // N -- List ; removes from queue
void glas_reg_queue_unread(glas*, char const*); // List --  ; prepends to head of queue
void glas_reg_queue_write(glas*, char const*); // List -- ; appends to tail of queue
void glas_reg_queue_peek(glas*, char const*); // N -- List ; read copy unread

/**
 * Treat register as a bag.
 * 
 * The register must contain a list, representing a multiset, aka 'bag'.
 * 
 * Every bag operation is free to non-deterministically reorder the list
 * and insert or remove items at non-deterministic locations. The bag is
 * essentially distillation of non-determinism into a data structure.
 * 
 * The advantage of bags is that they support any number of concurrent
 * readers and writers. The only requirement is to avoid the case where
 * two readers concurrently grab the same item. The runtime can freely 
 * partition the bag between readers, and move data opportunistically or
 * heuristically between partitions.
 * 
 * Bags should be favored over queues where order is irrelevant.
 */
void glas_reg_bag_read(glas*, char const*); // -- Data
void glas_reg_bag_write(glas*, char const*); // Data --
void glas_reg_bag_peek(glas*, char const*); // -- Data; as read copy write

/**  
 * TBD: More structures I'm interested in accelerating:
 * 
 * We can feasibly treat a register containing a dict as a key-value
 * database, with fine-grained read-write conflicts per key.
 * 
 * We can support a few CRDTs, allowing multiple transactions to read 
 * and write their own replicas concurrently, synchronizing between
 * transactions. Especially valuable for distributed runtimes.
 */

/**
 * TBD: Virtual Registers
 * 
 * We could feasibly use registers as proxies for client resources in 
 * read-write conflict analysis. But I lack a clear use-case. Later, if 
 * I introduce callbacks for precommit and commit, perhaps this feature
 * can be leveraged effectively?
 */

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
void glas_list_split(glas*);        // (L++R) (L len) -- L R
void glas_list_append(glas*);       // L R -- (L++R)
void glas_list_rev(glas*);          // reverse order of list

/**
 * Bitstring Operations
 */
void glas_bits_len(glas*);
void glas_bits_split(glas*);
void glas_bits_append(glas*);
void glas_bits_rev(glas*); // reverse order of bits
void glas_bits_invert(glas*); // flip 0 to 1 and vice versa

/**
 * Bitstring-Binary Conversions
 * 
 * The 'from_bin' operation converts binary to bitstring. Each byte
 * is translated to an 8-bit octet by adding a zeroes prefix, then 
 * appended, preserving order (msb to lsb, first byte to last).
 * 
 * The 'to_bin' operation simply does the opposite. It's an error on
 * a bitstring that isn't a multiple of 8 bits in length.
 * 
 * Note: Bitstrings receive far less optimization than binaries. They
 * are compactly represented
 */
void glas_bits_from_bin(glas*); // Binary -- Bitstring
void glas_bits_to_bin(glas*);   // Bitstring -- Binary

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

/**
 * Shrubs 
 * 
 * The runtime supports a simple encoding of trees into binaries. This
 * is useful for compact structure near leaf nodes within a tree, but it 
 * can also be useful for pushing or extracting plain glas data.
 * 
 *    00 - leaf
 *    01 - branch, followed by left then right shrubs
 *    10 - left stem, followed by shrub
 *    11 - right stem, followed by shrub
 *
 * A final suffix of zeroes may be truncated or padded as needed to 
 * represent a complete binary. For example, '01' becomes '01 00 00'
 * which translates to singleton list of the unit value, also the glas
 * representation for 'true'.
 *  
 * Not all binaries represent valid shrubs. Abstract data of any 
 * sort cannot be converted.
 */
void glas_shrub_from_bin(glas*); 
void glas_shrub_to_bin(glas*);

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


