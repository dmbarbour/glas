/**
 * Use glas runtime as a library.
 * 
 * On `glas_thread_new`, the client (your program) receives a `glas*`
 * context. This represents a remote-controlled glas coroutine that 
 * begins with an empty namespace, stack, and auxilliary stash.
 * 
 * Efficient data exchange with the runtime uses binaries or bitstrings.
 * This API provides means to tease apart or construct other data types,
 * but bulk binary transfers .
 * 
 *  require translation to
 * exchange. There are some operations support translation. 
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
 * mt-safety: Each glas thread must be used in a single-threaded manner,
 * but may be shared via mutex. Separate glas threads are fully mt-safe.
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
 * The default initializer.
 * 
 * A user configuration is sought in:
 * 
 * - GLAS_CONF environment variable     if defined
 * - ${HOME}/.config/glas/conf.glas     on Linux
 * - %AppData%\glas\conf.glas           on Windows (eventually)
 * 
 * This initializer operates on the namespace. The data stack is not
 * modified. It binds primitives to "%", the compiled user configuration 
 * to "conf.", then rebinds "%env." to final "conf.env.". Will bootstrap 
 * %env.lang.glas if possible, otherwise leaving the built-in versions.
 * 
 * This prepares a client for running configured applications or loading 
 * scripts.
 */
void glas_init_default(glas*);

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
 * Appears in warning messages, stack traces, and so on. Definitions and
 * forks may capture the current name of the thread that created them. 
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
void glas_choice(glas* origin, size_t N, void* client_arg, 
    void (*callback)(glas* clone, size_t index, void* client_arg));


/***************************
 * MEMORY MANAGEMENT
 **************************/

/**
 * Reference-counting shared objects.
 * 
 * Abstract reference counting is used for foreign pointers, callbacks,
 * and zero-copy binaries. The count is abstracted through a method to
 * incref or decref one step at a time. The refct_obj is separated from
 * the data to support flexible indirection.
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
 * Test for existence of definitions.
 * 
 * The has_def variant returns true if the specified name is defined.
 * The has_prefix variant returns true if at least one name with the
 * prefix is definied. Namespaces are lazily loaded, so testing for
 * definitions unnecessarily may consume a lot of resources.
 * 
 * Consider use of `glas_call_prepare` instead, using runtime worker
 * threads to load definitions in the background.
 */
bool glas_ns_has_def(glas*, char const* name);

/**
 * Utility. Hide names or prefixes from the thread namespace.
 * 
 * The namespace is lexically scoped, thus definitions may be reachable
 * indirectly through other definitions. Unreachable definitions may be
 * garbage collected by the runtime.
 */
void glas_ns_hide_def(glas*, char const* name);
void glas_ns_hide_prefix(glas*, char const* prefix);

/**
 * Define data.
 * 
 * This pops non-linear data from the stack then assigns a specified 
 * name. Calling the name pushes a copy of the data onto the stack.
 * 
 * Note: The thread namespace can be useful as a logically-immutable 
 * data context. Shadowing names allows GC of the older definitions, but
 * does not affect past forks or callbacks, only future operations. If 
 * you do need mutability, see registers.
 */
void glas_ns_data_def(glas*, char const* name);

/**
 * Prefix-to-prefix Translations.
 * 
 * The glas namespace model uses prefix-to-prefix translations for many
 * purposes. Within C, we'll simply represent these as an array of pairs
 * of strings terminating with NULL in the lhs. Example:
 * 
 *   { {"bar.", "foo."}, { "$", NULL }, { NULL, NULL } }
 * 
 * During lookup, the runtime will find the longest matching lhs prefix
 * then rewrite to rhs. If rhs is NULL, name is undefined. Otherwise, we
 * rewrite the matched prefix to rhs then move on. The runtime speeds
 * this up via caching.
 * 
 * Prefix-to-prefix translations have an obvious weakness: translation
 * of 'bar' to 'foo' also converts 'bard' to 'food'. To mitigate, the
 * runtime implicitly adds a ".." suffix onto *every* name. Translations
 * may take advantage: "bar." applies to both "bar" and "bar.x", and
 * "bar.." is very unlikely to be a prefix of another name by accident.
 */
typedef struct { char const *lhs, *rhs; } glas_ns_tl;

/**
 * Apply translation to current namespace.
 * 
 * All future access to names is translated through this TL. Unreachable
 * definitions can be collected. This can flexibly copy or hide parts of
 * the namespace.
 */
void glas_ns_tl_apply(glas*, glas_ns_tl const*);

/**
 * Push translation onto the stack as AST's TL type.
 */
void glas_ns_tl_push(glas*, glas_ns_tl const*); // -- TL

/** 
 * Define by evaluation.
 * 
 * See GlasNamespaces.md for details on the AST representation. This
 * operation pops an AST off the stack, validates the AST structure,
 * then lazily evaluates in the thread namespace (or under an optional
 * translation), then assigns the given name.
 * 
 * Note that any namespace AST can be evaluated and defined, but not all
 * are valid as callable programs. They can be useful for defining ever
 * larger constructs.
 * 
 * Note: Evaluates in thread namespace when translation is NULL.
 */
void glas_ns_eval_def(glas*, char const* name, glas_ns_tl const*);

/**
 * Define many names by evaluation.
 * 
 * Almost the same as eval_def, except the AST must evaluate to a 
 * reified environment. Glas namespaces reify environments with the
 * built-in 'e:()' AST node, lazily returning a structure like 
 * `{ "x" = def_of_x, "y" = def_of_y, ... }`.
 * 
 * In this case, we'll bind this reified environment to the prefix. For
 * example, use prefix "foo." to define "foo.x" and "foo.y" and so on.
 * Note: There is no implicit separator between prefix and name. 
 * 
 * All definitions previously reachable through the prefix are hidden.
 * That is, there is no merge of definitions.
 */
void glas_ns_eval_prefix(glas*, char const* prefix, glas_ns_tl const*);

/**
 * Rewrite thread namespace by evaluation.
 * 
 * Similar to eval_prefix, except the AST must evaluate to a namespace
 * function of type `Env -> Env`. We then apply this to a specified
 * prefix. 
 * 
 * This is a very flexible operation, capable of selectively merging,
 * overriding, and hiding definitions. Of course, it's also the hardest 
 * to control.
 */
void glas_ns_eval_apply(glas*, char const* prefix, glas_ns_tl const*);

/**
 * Utilities for namespace construction.
 * 
 * Namespace ASTs are ultimately plain old glas data on the stack.
 */
void glas_ns_ast_name_push(glas*, char const*); // -- Name ; just a binary 
void glas_ns_ast_apply(glas*);  // ArgAST OpAST -- AST = (OpAST, ArgAST)
void glas_ns_ast_tl(glas*); // ScopedAST TL -- AST = t:(TL, ScopedAST)
void glas_ns_ast_fn(glas*); // BodyAST Name -- AST = f:(Name, BodyAST)
void glas_ns_ast_env(glas*); // -- AST = e:() ; refies current environment
void glas_ns_ast_prefix_push(glas*, char const*); // -- Prefix ; just a binary
void glas_ns_ast_bind(glas*); //  BodyAST Prefix -- AST = b:(Prefix, AST)
void glas_ns_ast_anno(glas*); // BodyAST AnnoAST -- AST = a:(AnnoAST, BodyAST)
void glas_ns_ast_ifdef(glas*); // ElseAST ThenAST Name -- AST = c:(Name,(ThenAST, ElseAST))
void glas_ns_ast_fix(glas*); // OpAST -- AST = y:OpAST
void glas_ns_ast_data(glas*); // Data -- AST = d:Data   ; must be non-linear

/**
 * Composite constructors for common combinators.
 * 
 * These constructors produce closed terms, i.e. the AST doesn't depend
 * on any names in the evaluation environment.
 */
void glas_ns_ast_op_tag(glas*, char const* tag); // op to add tag to an AST
void glas_ns_ast_op_untag(glas*, char const* tag); // op to remove tag from AST
void glas_ns_ast_op_extract(glas*, char const* name); // op to extract name from Env
void glas_ns_ast_op_tl(glas*, glas_ns_tl const*); // op to translate an Env
void glas_ns_ast_op_seq(glas*); // op for function composition (first arg applies last)

/**
 * Isolate AST from the evaluation environment.
 * 
 * Mechanically, wraps AST with `t:({ "" => NULL }, AST)`. This enforces
 * an assumption that an AST represents a closed term.  
 */
void glas_ns_ast_close(glas*);

/**
 * Define programs using callbacks.
 * 
 * The callback function receives a dedicated `glas*` callback thread.
 * 
 * This thread provides access to two namespaces: a host namespace that
 * serves as a lexical closure at definition time, and a caller prefix
 * that provides access to implicit parameters, registers, and effects
 * handlers. Both bindings are subject to translation.
 * 
 * The caller's data stack is also visible to the specified input arity.
 * 
 * In general, the callback may commit multiple steps. However, there
 * are caveats and constraints: 
 * 
 * - If a callback commits any step, it must commit just before return.
 *   Pending actions are aborted, warning once per definition.
 * 
 * - Attempts to commit within an atomic section are atomicity errors.
 *   If a callback must commit to be useful, try the 'no_atomic' flag.
 * 
 * The callback may fork threads, but the caller namespace is valid only
 * for duration of the call. To support this, the caller waits for all
 * forks to either exit or or detach (see below).
 * 
 * Note: An uncreated error is possible prior to commit if the caller
 * was aborted. An unrecoverable arity error is possible if a callback
 * does not respect its declared stack arity after committing. Use of
 * a NULL callback will cause an error where called, not when defined.
 */
typedef struct {
    bool (*prog)(glas*, void* client_arg);
    void* client_arg;           // opaque, passed to operation
    glas_refct refct;           // integrate GC for client_arg
    char const* caller_prefix;  // e.g. "$"; shadows host names
    uint8_t ar_in, ar_out;      // data stack arity (enforced!)
    bool no_atomic;             // forbid calls in atomic sections
    char const* debug_name;     // appears in stack traces, etc.
} glas_prog_cb;
void glas_ns_cb_def(glas*, char const* name, glas_prog_cb const*, glas_ns_tl const* host_ns);

/**
 * Lazy linking for an entire namespace of callbacks.
 * 
 * A linker callback is invoked only if the name is referenced, and must
 * write the glas_prog_cb into a provided output parameter. It is called 
 * at most once for each name, caching results. Link may fail, returning 
 * false, in which case the runtime treats the name as undefined.
 */
typedef struct {
    bool (*link)(char const* name, glas_prog_cb*, void* client_arg);
    void* client_arg;
    glas_refct refct;
} glas_link_cb;
void glas_ns_cb_prefix(glas*, char const* prefix, glas_link_cb const*, glas_ns_tl const*);

/**
 * Detach a callback thread.
 * 
 * The `glas*` argument - the callback thread - starts with a special
 * 'attached' status bound to the caller namespace. Exception: when the
 * caller_prefix is NULL, a callback thread begins detached.
 * 
 * Upon detaching, the callback thread loses access to its caller's 
 * environment. Names bound to the caller are treated as undefined. To
 * fully detach, the callback thread must commit the detach operation.
 * 
 * This is mostly relevant for forks of the callback thread. Those forks
 * may run several steps with access to the caller, detach, commit, then
 * continue running concurrently with the caller.
 * 
 * Note: Attempting to detach when not attached may receive a warning,
 * albeit at most once per callback definition. 
 */
void glas_thread_detach(glas*);

/**
 * Fork a thread in the detached state.
 * 
 * This is useful in context of atomic operations. There is no way to
 * fully detach without committing, but a fork created in the detached
 * state doesn't need to detach. The callback may fork detached threads,
 * pass them to on_commit for deferred operation, then return.
 */
void glas_thread_fork_detached(glas*);


/*********************************
 * BULK DEFINITION AND MODULARITY
 *********************************/

/**
 * Load built-in definitions.
 * 
 * This binds built-in definitions to a specified prefix, by convention
 * "%". Includes annotations, accelerators, program constructors, etc..
 * 
 * Also includes built-in front-end compilers under %env.lang.* for at
 * least the 'glas' file extension.
 * 
 * Note: This doesn't include names managed by front-end compilers, such
 * as %src, %env.*, %arg.*, and %self.*. 
 */
void glas_load_builtins(glas*, char const* prefix);

/**
 * Configure the runtime-global file loader.
 * 
 * The %macro primitive enables namespaces to load files. Intercepting
 * this loader enables a client to redirect files or integrate client
 * local resources into a compilation.
 * 
 * When 'intercept' returns false, the runtime uses a built-in loader.
 * Otherwise, the runtime uses 'tryload'. This decision extends to all
 * relative file paths, thus is only decided for absolute paths or URIs.
 * 
 * If 'tryload' returns false, it's treated as a failure, like a missing
 * file. The compiler can recover from that. Alternatively, tryload may
 * return true with a NULL ppBuf to represent divergence errors.
 * 
 * This API relies on zero-copy transfer. See `glas_binary_peek_zc`.
 */
typedef struct {
    bool (*intercept)(char const* FilepathOrURI);
    bool (*tryload)(char const* FilepathOrURI, 
        uint8_t const** ppBuf, size_t* len, glas_refct*);
} glas_file_cb;

void glas_rt_set_loader(glas_file_cb const*);

/**
 * Flexible 'file' references.
 * 
 * - src: usually a filepath. May be file text in some cases.
 * - lang: NULL, or a file extension (for override or embedded)
 * - embedded: if true, treat src as file content, not file name
 * 
 * There is a limitation that embedded src cannot contain NULL bytes.
 * If you need greater flexibility, use `glas_file_intercept` to name
 * a virtual file like "/vfs/mysource.mylang".
 */
typedef struct {
    char const* src;
    char const* lang;
    bool embedded;
} glas_file_ref;


/**
 * Load file as module.
 * 
 * This operation loads a file binary then compiles it according to its
 * file extension, i.e. based on '%env.lang.FileExt' or defaulting to a
 * built-in.
 * 
 * Files are expected to compile to the namespace AST, i.e. a plain old
 * data structure. An AST representing a closed-term namespace operation
 * of type `Env->Env`, tagged "module". Constructing this is the job of
 * front-end compilers.
 * 
 * The loader performs lightweight verification of the AST, then wraps
 * it to add '%src' and fixpoint '%self.*', then links to the thread's
 * '%*' namespace (under a given translation), then evaluates and binds
 * to a given prefix. 
 * 
 * By convention, front-end compilers should treat '%env.*' as implicit 
 * parameters (propagated across imports), and '%arg.*' as explicit. The
 * translation can thus hide or provide parameters.
 */
void glas_load_file(glas*, char const* prefix, glas_file_ref const*, glas_ns_tl const*);

/**
 * Load user configuration file.
 * 
 * A user configuration is the heart of a glas system. One big namespace
 * per user, inheriting from and extending community configurations.
 * 
 * Loading a configuration file requires special handling for bootstrap
 * of user-defined compilers and fixpoint of the environment. Otherwise,
 * it behaves as loading a regular file.
 * 
 * This special handling:
 * 
 * - the configuration's 'env.*' output is visible as '%env.*' input.
 * - the runtime will attempt to bootstrap the configuration's front-end
 *   compilers, using whatever starts in '%env.lang.*' as the built-ins.
 */
void glas_load_config(glas*, char const* prefix, glas_file_ref const*, glas_ns_tl const*);


/**************************
 * CALLING FUNCTIONS
 **************************/
/**
 * Run a defined program.
 * 
 * The called program receives access to the caller's data stack and 
 * namespace, subject to translation. We may translate the namespace;
 * NULL for no translation.
 * 
 * The `_atomic` variant forces the call to be atomic, i.e. the call
 * cannot commit a real world step; attempting is an atomicity error.
 * 
 * Not every definition can be called as a program. Some definitions may
 * be useful only for namespace eval. But we can call:
 * 
 * - programs, tagged "prog", namespace type `Env -> Program`.
 * - data, tagged "data", namespace type is embedded data
 * - callback definitions act as programs
 *  
 */
void glas_call(glas*, char const* name, glas_ns_tl const*);
void glas_call_atomic(glas*, char const* name, glas_ns_tl const*);

/**
 * Ask runtime to prepare definitions.
 * 
 * This operation is intended to mitigate lazy loading of very large
 * namespaces, asking runtime worker threads to load in the background.
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
 * each step the stack contains one checkpoint, equivalent to abort.
 * 
 * During the step, the program may save or push new checkpoints. Save
 * will replace the most recent checkpoint. Push will add a new one to
 * the checkpoint stack. Save and push may fail for the same reasons as
 * commit may fail: conflict and errors. 
 * 
 * Loading a checkpoint rewinds context state to the moment immediately
 * before that checkpoint was saved or pushed. Thus, in case of retry, a
 * client must again save or push the checkpoint and check for failure.
 * Load executes 'on_abort' operations created after the checkpoint to
 * support partial rollback.
 * 
 * Checkpoints are relatively cheap, but they may encourage long-running
 * transactions that run greater risk of conflict. 
 */
bool glas_checkpoint_save(glas*); // overwrite last checkpoint on success
bool glas_checkpoint_push(glas*); // push new checkpoint onto stack on success
void glas_checkpoint_drop(glas*); // drop a pushed checkpoint
void glas_checkpoint_load(glas*); // update state to recent checkpoint

/**
 * Timeouts.
 * 
 * Timeouts add a quota error to the current thread when it fails to 
 * set the timeout again or commit in the allotted duration. A timeout 
 * is canceled by setting 0. 
 * 
 * Timeouts cannot be fully respected in the final commit phases, e.g.
 * for a shared database. They must be considered best effort.
 * 
 * Step timeouts do not carry between steps. Checkpoint timeouts are
 * similar, being automatically reset to 0 after commit or checkpoint.
 */
void glas_step_timeout(glas*, uint64_t usec);
void glas_checkpoint_timeout(glas*, uint64_t usec);


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
    GLAS_E_DATA_QTY         = 0x100000, // mostly for queue reads
    GLAS_E_UNDERFLOW        = 0x200000, // stack or stash underflow
    GLAS_E_ARITY            = 0x400000, // callback arity violation; unrecoverable if committed
} GLAS_ERROR_FLAGS;

GLAS_ERROR_FLAGS glas_errors_read(glas*);           // read error flags
void glas_errors_write(glas*, GLAS_ERROR_FLAGS);    // monotonic via bitwise 'or'


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
 * Data is sealed by a key. Currently, keys are limited to registers.
 * To unseal the data requires naming the same register, though often by
 * a different name. It's an error to observe or access the data without 
 * first unsealing it.
 * 
 * The register is not modified by its use as a key. It mostly serves as
 * a robust, unforgeable identity source. But it also serves as a source
 * of ephemerality: The runtime reports ephemerality errors when writing
 * short-lived data into long-lived registers. Data sealed by a register
 * can be garbage-collected if that register becomes unreachable.
 * 
 * Mechanically, sealing data involves an allocation, wrapping the data. 
 * Unseal unwraps the data. There's a small performance tradeoff.
 */
void glas_data_seal(glas*, char const* key);
void glas_data_unseal(glas*, char const* key);

/** 
 * A dynamic approach to abstract, linear data objects.
 * 
 * The _linear variants seal data as above, but also forbid the sealed
 * data from being copied or dropped, raising linearity errors. This is
 * useful for enforcing protocols, e.g. ensuring files are closed after 
 * opening.
 * 
 * Dynamic linearity isn't perfect. There are ways to drop data: clients
 * can exit a thread with linear data on the stack, or remove registers
 * from scope that contain linear data. Also, in context of transactions
 * and non-deterministic choice, linear objects may have many references
 * without ever being *logically* copied.
 * 
 * Despite those caveats, linearity remains useful.
 */
void glas_data_seal_linear(glas*, char const* key);
void glas_data_unseal_linear(glas*, char const* key);

/*****************************************
 * REGISTERS
 ****************************************/

/**
 * New registers.
 * 
 * In future operations, prefix refers to a fresh volume of registers.
 * Every name in prefix is defined, i.e. we logically have an infinite
 * set of registers. But they are lazily allocated by the runtime and
 * initialized to zero on demand.
 * 
 * A register that becomes unreachable, e.g. due to name shadowing, may
 * be garbage collected.
 */
void glas_load_reg_new(glas*, char const* prefix);

/**
 * Associative registers.
 * 
 * Introduces a unique space of registers identified by an ordered pair 
 * of registers. The same registers always name the same space.
 * 
 * A primary use case is abstract data environments: instead of sealing
 * data, we can seal entire volumes of registers. This avoids the wrap
 * and unwrap mechanic of sealed data, and enables fine-grained conflict
 * analysis on individual registers in the volume.   
 */
void glas_load_reg_assoc(glas*, char const* prefix, char const* r1, char const* r2);

/**
 * Runtime-global registers.
 * 
 * Shared registers bound to static globals in the runtime library. This
 * allows for communication between glas threads of different origins.
 * 
 * Global registers serve as a useful proxy for client resources, such
 * as for operations queues in `glas_step_on_commit`.
 */
void glas_load_reg_global(glas*, char const* prefix);

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
 * Swap (read-write) data between stack and register.
 * 
 * This is the primitive operation on registers in the program model. It
 * is friendly for linearity, but awkward to use directly.
 */
void glas_reg_rw(glas*, char const*); // A -- B

/**
 * The basic get/set operations.
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
 * requested are available. A writer does not read register content.
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
 * Indexed registers containing a dict or array, tracking fine-grained
 * read-write conflicts per index.
 * 
 * CRDTs, allowing multiple transactions to concurrently read and write 
 * their own replicas, synchronizing between transactions. Especially 
 * valuable for distributed runtimes.
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
 * BASIC DATA MANIPULATION
 ********************************/

/**
 * Primitive Stack Manipulations.
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
 * part of the stack from a subprogram. Callback threads do not receive 
 * access to a caller's stash, and items remaining in a callback thread
 * stash are orphaned upon return.
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
bool glas_data_is_ratio(glas*);     // dicts of form { n:Bits, d:Bits }, non-empty d 

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
 * The 'of_bin' operation converts binary to bitstring. Each byte
 * is translated to an 8-bit octet by adding a zeroes prefix, then 
 * appended, preserving order (msb to lsb, first byte to last).
 * 
 * The 'to_bin' operation simply does the opposite. It's an error on
 * a bitstring that isn't a multiple of 8 bits in length.
 */
void glas_bits_of_bin(glas*); // Binary -- Bitstring
void glas_bits_to_bin(glas*); // Bitstring -- Binary

/**
 * Dict Operations 
 * 
 * For convenience and performance, there are two forms for some APIs.
 * It is recommended to provide the label directly to the dictionary
 * operation rather than encoding the label as a separate step.
 */
void glas_dict_insert(glas*);       // Item Record Label -- Record'
bool glas_dict_remove(glas*);       // Record Label -- Item Record' | FAIL
void glas_dict_insert_label(glas*, char const* label); // Item Record -- Record'
bool glas_dict_remove_label(glas*, char const* label); // Record -- Item Record' | FAIL
// TBD: effecient iteration over dicts, merge of dicts

/**
 * Rationals. TBD
 */

/**
 * Arithmetic. TBD.
 */


/******************************
 * RUNTIME REFLECTION
 *****************************/

/**
 * Background calls, a transactional escape.
 * 
 * Logically, a bgcall runs *before* the caller's current step. This is
 * useful for fetching data that could, in principle, have been safely 
 * collected and cached even without a request, such as HTTP GET or to
 * read a file. It's also useful for triggering 'lazy' work that could, 
 * in principle, have been performed previously.
 * 
 * Mechanically, bgcall pops an argument off the data stack, runs an op
 * using a runtime worker thread, then returns the result. Argument and
 * result must be non-linear data. If op diverges, so does the bgcall.
 * 
 * The op runs detached from the caller, but the runtime provides a call
 * environment that includes a "canceled" method to query cancellation.
 * 
 * The caller may be interrupted before the bgcall operation completes.
 * For example, a timeout or conflict could abort the call. However, if
 * the bgcall op has already committed once, it may continue running to
 * completion, or it may query for cancellation. There is an opportunity
 * to attach to running bgcall, via matching op and argument, before it 
 * observes cancellation.
 * 
 * A bgcall can conflict with its own caller, forcing caller to abort.
 * This has potential for thrashing in context of retries, but that is
 * relatively easy to detect, debug, and design around.
 */ 
void glas_refl_bgcall(glas*, char const* op);

/** TBD
 * - memory and gc (profiling, etc.)
 * - logging and profiling
 *   - client ability to interact with these, too.
 * - debugging, stack traces
 * - inspect and kill operations
 */


/**
 * Run library's built-in tests.
 */
bool glas_rt_run_builtin_tests();


#define GLAS_H
#endif


