/**
 * Use glas runtime as a library.
 * 
 * On `glas_thread_new`, the client (your program) receives a `glas*`
 * context. This represents a remote-controlled glas coroutine that 
 * begins with an empty namespace, stack, and auxilliary stash.
 * 
 * Efficient data exchange with the runtime is possible via zero-copy
 * binaries. Other structures may require dozens of calls to construct
 * or analyze.
 * 
 * Error handling is transactional: the client performs a sequence of
 * operations on a thread then commits the step. In case of error or
 * conflict, the step fails to commit. But the client can rewind and 
 * retry, or try something new. The on_commit and on_abort callbacks 
 * simplify integration with C.
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
 * a stack, stash, and a lexically scoped namespace, plus bookkeeping 
 * for transactions, checkpoints, and errors. The thread awaits commands
 * from the client, and most commands are synchronous (awaiting result).
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
 * is possible origin aborts the step, in which case fork is 'canceled' 
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
 * After a candidate is chosen, all running clones are canceled, which
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
 * Abstract reference counting is used in callbacks, pointers, zero-copy
 * binaries. Reference counts shall be mt-safe, and are pre-incremented
 * before crossing the API, i.e. such that a decref indicates release of
 * the referenced resource. The `refct_obj` is separate from the target
 * to permit indirection, e.g. logical slices of binaries.
 * 
 * The `refct_upd` function may be NULL if an object does not need to be
 * managed. When reading data from the runtime, `glas_refct*` arguments
 * may be NULL assuming the client maintains a reference to the resource
 * on stack or in stash to guard against garbage collection.
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
 * Lexical Namespace Scope
 * 
 * The glas thread maintains a stack of namespace scopes. Push creates a
 * backup of the current namespace while pop restores to last push. This
 * is a O(1) operation via logical copy.
 * 
 * Scopes are maintained across steps, but are not shared with forks or
 * callbacks.
 */
void glas_ns_scope_push(glas*);
void glas_ns_scope_pop(glas*);

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
 * 
 * Note: These are simple wrappers around glas_ns_tl_apply.
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
 * rewrite the matched prefix to rhs then move on. Behavior is undefined
 * if an lhs prefix appears more than once.
 * 
 * Prefix-to-prefix translations have an obvious weakness: translation
 * of 'bar' to 'foo' also converts 'bard' to 'food'. To mitigate, glas
 * systems implicitly add a ".." suffix onto every name. This doesn't 
 * guarantee prefix-uniqueness, but it resists accident. It also enables
 * translation of "bar." to include "bar" and "bar.x".
 * 
 * Note: a NULL `glas_ns_tl const*` is equivalent to {{ NULL, NULL }},
 * i.e. the no-op or identity translation.
 */
typedef struct { char const *lhs, *rhs; } glas_ns_tl;

/**
 * Apply translation to thread namespace.
 * 
 * This affects future operations on the glas thread. Definitions that
 * become unreachable may be garbage collected after step commit.
 */
void glas_ns_tl_apply(glas*, glas_ns_tl const*);

/**
 * Push a representation of a translation onto the data stack
 */
void glas_ns_tl_push(glas*, glas_ns_tl const*); // -- TL

/** 
 * Define by evaluation.
 * 
 * Pop an AST representation off the stack, validate the AST structure,
 * lazily evaluate in context of a translated thread namespace. 
 * 
 * Note: ASTs become second-class in the program layer once evaluated.
 */
void glas_ns_eval_def(glas*, char const* name, glas_ns_tl const* eval_env);

/**
 * Define many names by evaluation.
 * 
 * Like eval_def, except the AST must evaluate to an environment, and we
 * bind this environment of definitions to a prefix. For example, if the
 * prefix is "foo.", then "foo.x" will look for "x" in the environment,
 * falling back to a prior definition of "foo.x".
 */
void glas_ns_eval_bind(glas*, char const* prefix, glas_ns_tl const* eval_env);

/**
 * Rewrite thread namespace by evaluation.
 * 
 * Like eval_bind, except the AST must represent an `Env->Env` function.
 * We then apply this to a specified prefix.
 */
void glas_ns_eval_apply(glas*, char const* prefix, glas_ns_tl const*);

/**
 * Utilities for namespace construction.
 */
void glas_ns_ast_mk_name(glas*, char const* Name); // -- Name ; just a binary 
void glas_ns_ast_mk_apply(glas*);  // ArgAST OpAST -- AST = (OpAST, ArgAST)
void glas_ns_ast_mk_tl(glas*, glas_ns_tl const*); // AST -- AST = t:(TL, ScopedAST)
void glas_ns_ast_mk_fn(glas*, char const* Var); // BodyAST -- AST = f:(Var, BodyAST)
void glas_ns_ast_mk_env(glas*); // -- AST = e:() ; refies current environment
void glas_ns_ast_mk_bind(glas*, char const* Prefix); // AST -- b:(Prefix, AST)
void glas_ns_ast_mk_anno(glas*); // BodyAST AnnoAST -- AST = a:(AnnoAST, BodyAST)
void glas_ns_ast_mk_ifdef(glas*, char const* Name); // R L -- AST = c:(Name,(L, R))
void glas_ns_ast_mk_fix(glas*); // OpAST -- AST = y:OpAST
void glas_ns_ast_mk_data(glas*); // Data -- AST = d:Data   ; must be non-linear

/**
 * Composite constructors for common combinators.
 * 
 * These constructors produce closed terms, i.e. the AST doesn't depend
 * on any names in the evaluation environment. In many cases, it's best
 * to construct a tag combinator and apply it rather than wrap an AST
 * directly because the former better resists accidental name shadowing. 
 */
void glas_ns_ast_mkop_tag(glas*, char const* tag); // op to add tag to an AST
void glas_ns_ast_mkop_untag(glas*, char const* tag); // op to remove tag from AST
void glas_ns_ast_mkop_extract(glas*, char const* name); // op to extract name from Env
void glas_ns_ast_mkop_tl(glas*, glas_ns_tl const*); // op to translate an Env
void glas_ns_ast_mkop_seq(glas*); // op for function composition (first arg applies last)

/**
 * Isolate AST from the evaluation environment.
 * 
 * Mechanically, wraps AST with `t:({ "" => NULL }, AST)`. This enforces
 * an assumption that an AST represents a closed term before evaluation.
 */
void glas_ns_ast_mk_closed_term(glas*); // AST -- AST (closed)

/**
 * Define programs using callbacks.
 * 
 * The callback function receives a dedicated `glas*` callback thread to
 * receive arguments and write results through data stack and namespace.
 * 
 * The namespace has two components: closure of the host namespace where 
 * the callback is defined, and a caller-provided namespace supporting 
 * pass-by-ref registers and algebraic effects handler. Translations can
 * restrict and route these namespaces. Updates to a callback namespace
 * are localized to each callback. The glas program model does not have 
 * first-class functions, but algebraic effects serve a similar role.
 * 
 * The specified arity (ar_in, ar_out) determines how many items are
 * moved from the caller's data stack then back to the caller on return.
 * 
 * In general, callbacks may commit multiple steps. Caveats: 
 * 
 * - If a callback commits even once, it must commit just before return.
 *   This is because we cannot robustly backtrack into C functions. To 
 *   simplify, pending operations are aborted with a warning.
 * 
 * - Callbacks cannot commit within an atomic context. Attempting to do
 *   so always results in an atomicity error. If a callback must commit,
 *   use the 'non_atomic' flag to detect errors early.
 * 
 * The tradeoff is that atomic callbacks are more widely usable but rely
 * heavily on the on_commit and on_abort events, whereas non-atomic is a
 * more convenient fit for C and synchronous request-response patterns.
 * 
 * A callback may fork threads, but the caller's namespace is valid only
 * for duration of the call. To support this, the caller waits for all
 * forks to either exit or detach then commit (see below).
 * 
 * In general, a callback may be canceled because the caller has not yet
 * committed to perform the call. For non-atomic callbacks, this will be
 * resolved on first commit, which also commits to performing the call.
 * 
 * To integrate with partial evaluation, callbacks have a 'static_eval'
 * option with states reject (default), accept, or require. If accepted,
 * callbacks are opportunistically evaluated at link time if ar_in stack
 * elements are available. If required, error unless evaluated at link 
 * time. Link-time evaluation is lazy via logical background thread (see
 * refl_bgcall). Note: reject unless commutative, idempotent, cacheable!
 * 
 * Notes: If cb is NULL, treats as `%error` definition. If caller_prefix
 * is NULL, caller namespace is unavailable. If debug_name is NULL, uses 
 * function pointer as opaque stand-in.
 */
typedef struct glas_prog_cb {
    bool (*cb)(glas*, void* client_arg);
    void* client_arg;           // opaque, passed to operation
    glas_refct refct;           // integrate GC for client_arg
    char const* caller_prefix;  // e.g. "$"; hides host names
    uint8_t ar_in, ar_out;      // data stack arity (enforced!)
    bool non_atomic;            // forbid use in atomic sections
    uint8_t static_eval;        // 0=avoid, 1=accept, 2=require
    char const* debug_name;     // appears in stack traces, etc.
} glas_prog_cb; // note: for extensibility, zero-fill fields that are not set!
void glas_ns_cb_def(glas*, char const* name, glas_prog_cb const*, glas_ns_tl const* host_ns);

/**
 * Lazy linking for an entire namespace of callbacks.
 * 
 * A linker callback is invoked if the name is referenced from a program
 * being prepared for call. It is called at most once per name, caching 
 * results. It is not guaranteed that the function will be called.
 * 
 * Link may fail, returning false, in which case the runtime treats the
 * name as undefined and may default to a prior definition under prefix.
 * Alternatively, link may succeed but set callback function to NULL, in
 * which case we treat as linking an invalid program.
 */
typedef struct glas_link_cb {
    bool (*link)(char const* name, glas_prog_cb* out, void* client_arg);
    void* client_arg;
    glas_refct refct;
} glas_link_cb;
void glas_ns_cb_bind(glas*, char const* prefix, glas_link_cb const*, glas_ns_tl const* host_ns);

/**
 * TBD: staged linking? tentative
 * 
 * Can feasibly provide static parameters, e.g. generating glas_prog_cb*
 * in context of a caller_prefix. This offers an alternative approach to
 * static evaluation, albeit limited to the namespace layer.
 * 
 * But it requires a relatively sophisticated API, and benefits compared
 * static_eval (in glas_prog_cb) together with foreign pointers are very
 * limited. Essentially moves one pointer from data stack to client_arg!
 * 
 * For now, defer this feature. Reconsider later.
 */

/**
 * Detach a callback thread.
 * 
 * This detaches the `glas*` thread from the caller's namespace. Access
 * to callbacks or registers defined in the caller's namespace raises an
 * error after detach.
 * 
 * This is most useful in context of forks. Normally, glas systems have
 * fork-join semantics. The caller will wait for threads forked during a
 * callback. But the caller may also continue after forks detach and
 * commit.
 */
void glas_thread_detach(glas*);

/**
 * Fork a thread in the detached state.
 * 
 * This is useful in context of atomic operations. A detached fork still
 * cannot commit before its origin commits the fork operation. However,
 * this supports creation of threads for background tasks upon commit.
 */
glas* glas_thread_fork_detached(glas*);

/*********************************
 * BULK DEFINITION AND MODULARITY
 *********************************/

/**
 * Bind built-in definitions to a prefix.
 * 
 * This binds built-in definitions to a specified prefix, conventionally
 * "%". Includes annotations, accelerators, program constructors, etc..
 * Also includes built-in front-end compilers under %env.lang.* for at
 * least the 'glas' file extension.
 * 
 * Note: This doesn't include names managed by front-end compilers, such
 * as %src, %env.*, %arg.*, and %self.*. 
 */
void glas_load_builtins(glas*, char const* prefix);


typedef enum glas_load_status {
    GLAS_LOAD_OK = 0,       // valid file read
    GLAS_LOAD_NOENT,        // specified file or folder does not exist
    GLAS_LOAD_ERROR,        // e.g. unreachable or permissions issues
} glas_load_status;

/**
 * Logical overlay of local filesystem.
 * 
 * This allows the client to virtualize some dependencies, redirecting
 * some source files to a local callback. Use cases include providing
 * files through a network, database, scripting, or as built-ins. This 
 * does not extend to DVCS resources, at the moment.
 * 
 * An overlay applies to a specified prefix. Files matching this prefix
 * are processed by the given loader function. Or, if loader is NULL, we
 * return to the default loader. Multiple overlays may be stacked, last
 * one wins, thus shorter prefixes should precede longer prefixes unless
 * the goal is to replace prior overlays.
 * 
 * In some cases, the runtime may search a folder. The load_dir callback
 * is used in this case, and should callback via 'emit' for each relative 
 * path, indicating whether it represents another folder.
 * 
 * Errors distinguish NOENT (file does not exist) and everything else. A
 * missing file can be 'compiled' by some front-end compilers, e.g. to
 * support projectional editors and live coding.
 */
typedef struct glas_file_loader {
    void* client_arg;
    glas_refct refct; // decref after loader becomes unreachable
    glas_load_status (*load_file)(char const* path, 
        uint8_t const** ppBuf, size_t* len, glas_refct*, 
        void* client_arg);
    glas_load_status (*load_dir)(char const* dir,
        void (*emit)(char const* path, bool isdir), 
        void* client_arg);
} glas_file_loader;
void glas_rt_file_loader(glas_file_loader const*, char const* prefix);

/**
 * Flexible 'file' references.
 * 
 * - src: usually a filepath, but is file content if 'embedded'
 * - lang: NULL, or a file extension (override or embedded)
 * - embedded: if true, treat src as file content, not file name
 *
 * This is mostly intended for naming user configurations and scripts.
 * Anything more sophisticated should use glas_rt_file_loader.
 */
typedef struct {
    char const* src;
    char const* lang;
    bool embedded;
} glas_file_ref;


/**
 * Load user configuration file.
 * 
 * A user configuration is the heart of a glas system: one big namespace
 * per user, inheriting from and extending community configurations.
 * 
 * Loading a configuration file requires special handling for bootstrap
 * of user-defined compilers and fixpoint of the environment. Otherwise,
 * it behaves as binding a script.
 * 
 * This special handling:
 * 
 * - the configuration's 'env.*' output is fed back as '%env.*' input.
 * - the runtime will attempt to bootstrap the configuration's front-end
 *   compilers, using whatever starts in '%env.lang.*' as the built-ins.
 */
void glas_load_config(glas*, char const* prefix, glas_file_ref const* src, glas_ns_tl const*);

/**
 * Load a script file.
 * 
 * After loading a configuration, script files can bind the configured
 * environment, constructing applications from a shared library. Scripts
 * receive access to their own definitions via fixpoint of `%self.*`.
 * 
 * The translation in this case should overlay config.env onto `%env.*`.
 */
void glas_load_script(glas*, char const* prefix, glas_file_ref const* src, glas_ns_tl const*);


/**************************
 * CALLING FUNCTIONS
 **************************/
/**
 * Call a defined program by name.
 * 
 * The runtime expects callable definitions to be tagged with a calling
 * convention. Tags are a namespace-layer design pattern, i.e. functions
 * that receive a record of adapters and select one or more to adapt the
 * definition. Recognized tags:
 * 
 * - "prog" for a program definition (verify then run)
 * - "data" for embedded data (push to top of stack)
 * - "call" for Env -> callable tagged Def 
 * 
 * Program verification should ultimately be configurable. In my vision,
 * the user configration will directly specify most verification code to
 * support gradual types, proof-carrying code, user-defined annotations.
 * But built-in support, e.g. arity checks and static assertions, may be
 * implemented by default.
 * 
 * Calls provide the thread's namespace translated through caller_env as
 * additional context. This supports higher-order programs, pass-by-ref
 * registers, algebraic effects. Within programs, call context is static 
 * while data stack is dynamic. But this API expresses dynamic context.
 * 
 * For non-atomic calls, there is feedback on whether the step commits.
 * This is important for backtracking: if a call commits, abort rewinds
 * and replays a program suffix to just after the call, and checkpoints 
 * are cleared. But clients may receive this feedback via step_on_commit
 * and set 'commits' to NULL.
 * 
 * Clients may request 'atomic' calls, equivalent to wrapping a program
 * with `%atomic`. This guarantees a call can be fully backtracked, but
 * may instead diverge with atomicity errors.
 */
void glas_call(glas*, char const* name, glas_ns_tl const* caller_env, bool* commits);
void glas_call_atomic(glas*, char const* name, glas_ns_tl const* caller_env);

/**
 * Ask runtime to prepare definitions.
 * 
 * This operation is intended to mitigate lazy loading of very large
 * namespaces, asking runtime worker threads to load in the background.
 */
void glas_call_prep(glas*, char const* name);


/*********************************
 * TRANSACTIONS AND BACKTRACKING
 *********************************/

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
 * Rewind a glas thread's state to start of the current step. To support 
 * client state rewind, try the `glas_step_on_abort` API.
 */
void glas_step_abort(glas*);

/**
 * Committed action.
 * 
 * For operations that are difficult to undo, clients may defer action 
 * until after commit. Although asynchronous operations are awkward in
 * their own ways, they're often easier than undo.
 * 
 * For transaction serializability, we write operations into queues. The
 * queue is then processed by a runtime worker thread. Clients may await
 * results indirectly, e.g. arrange an operation to signal a semaphore.
 * Any register name may be used as a queue name, but the register isn't
 * modified, used only for identity (cf reg_bind_assoc). A special case, 
 * the NULL queue is processed locally before return from step_commit.
 */
void glas_step_on_commit(glas*, char const* queue, void (*op)(void* arg), void* arg);

/**
 * Defer operations until abort.
 * 
 * This is mostly useful to clean up allocated memory, but it may find
 * other use cases. These are run before return from `glas_step_abort`.
 * In case of checkpoints, we'll also abort a suffix of operations on
 * load.
 * 
 * Order is reversed: last operation inserted is first executed. This
 * aligns with conventional stack unwind cleanup.
 */
void glas_step_on_abort(glas*, void (*op)(void* arg), void* arg);

/**
 * Checkpoints.
 * 
 * Checkpoints can be viewed as stack of hierarchical transactions. Push
 * starts a new transaction, load aborts that transaction, drop commits
 * to the checkpoint but not to the step. Load immediately runs on_abort 
 * ops since the checkpoint. (Note: drop does not run on_commit ops.)
 */
void glas_checkpoint_push(glas*); // copy context for the checkpoint
void glas_checkpoint_load(glas*); // 'abort', rewind to prior checkpoint
void glas_checkpoint_drop(glas*); // 'commit' to updates since checkpoint
void glas_checkpoint_clear(glas*); // drop all checkpoints

/**
 * Reactivity.
 * 
 * Requests a runtime callback when state observed by a step is updated.
 * One use case is awaiting changes before retrying a step. Rather than
 * immediate abort and retry, await relevant state changes before abort.
 * 
 * Unlike on_abort or on_commit, there is only one on_update callback.
 * It may be updated or canceled (via NULL op), and is implicitly reset
 * to NULL just before the callback, on successful commit, and on abort.
 * 
 * Note: Client state can be integrated via Virtual Registers.
 */
void glas_step_on_update(glas*, void(*op)(void* arg), void* arg);

/***************************************
 * ERRORS 
 **************************************/
/**
 * Error Summary. A bitwise `OR` of error flags.
 * 
 * A glas thread cannot commit steps while in error state. Most errors
 * can be recovered from via abort or loading a prior checkpoint. But a
 * few are unrecoverable, e.g. canceled forks or callbacks remain so. An
 * error will flag unrecoverable in this case.
 */
typedef enum GLAS_ERROR_FLAGS { 
    GLAS_NO_ERRORS          = 0x0000000,
    GLAS_E_UNRECOVERABLE    = 0x0000001, // abort won't fix unrecoverable errors

    // EXTERNAL ERRORS
    GLAS_E_CONFLICT         = 0x0000002, // concurrency conflicts; retry might avoid
    GLAS_E_CANCELED         = 0x0000004, // glas thread creation (callback or fork) aborted
    GLAS_E_QUOTA            = 0x0000008, // configured quota or timeout
    GLAS_E_IMPL             = 0x0000010, // incomplete implementation
    GLAS_E_CLIENT           = 0x0000080, // generic client-inserted error

    // OPERATION ERRORS
    GLAS_E_LINEARITY        = 0x0000100, // copy or drop of linear data
    GLAS_E_EPHEMERALITY     = 0x0000200, // ephemeral data shared beyond scope
    GLAS_E_ABSTRACTION      = 0x0000400, // direct observation forbidden
    GLAS_E_ATOMICITY        = 0x0000800, // e.g. commit in atomic context
    GLAS_E_ASSERT           = 0x0001000, // a check failed
    GLAS_E_UNDERFLOW        = 0x0002000, // stack underflow
    GLAS_E_OVERFLOW         = 0x0004000, // stack overflow
    GLAS_E_ARITY            = 0x0008000, // arity violation
    GLAS_E_TYPE             = 0x0010000, // runtime type errors
} GLAS_ERROR_FLAGS;

/**
 * Clients may read or write errors.
 * 
 * However, checking for conflict is expensive. To provide some control
 * over expensive tests, read may mask which errors are tested. Writing
 * errors applies to current checkpoint unless GLAS_E_UNRECOVERABLE is
 * indicated, in which case the flags add to unrecoverable error state.
 */
GLAS_ERROR_FLAGS glas_errors_read(glas*, GLAS_ERROR_FLAGS mask);
void glas_errors_write(glas*, GLAS_ERROR_FLAGS);

/*********************************************
 * DATA TRANSFER
 *********************************************/
/** 
 * Push a binary to the data stack.
 * 
 * The base version will copy the binary. The zero-copy (_zc) variant
 * will transfer a reference. Small binaries may be copied regardless.
 * The runtime assumes the client will not modify a zero-copy binary.
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
 * may flatten a rope-structured binary then return a reference. It is
 * undefined behavior for the client to mutate a zero-copy buffer.
 * 
 * The peek operation returns 'true' if end-of-list was reached and
 * everything available was read. The operation will return false with 
 * a partial result if data is only partially a valid binary. Peek does
 * not cause a runtime error even for invalid data.
 * 
 * Note: Arguably, flattening a binary is a copy. The zero-copy variant
 * has potential for zero-copy, i.e. to avoid redundant alloc and copy,
 * but it's best-effort and does not truly guarantee zero-copy.
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
 * type. Optionally, a pointer may be marked linear, in which case copy
 * and drop operations will raise an error (see `glas_data_seal`).
 * 
 * Operations:
 * - push: puts pointer on stack, optionally linear
 * - peek: read value of pointer, leave on stack
 * - pop: as peek then drop, can drop linear pointers
 * 
 * It is permitted to peek at a linear pointer, i.e. it does not count 
 * as a copy. But a linear pointer may be dropped only via pop.
 */
void glas_ptr_push(glas*, void*, glas_refct, bool linear);
bool glas_ptr_peek(glas*, void**, glas_refct*);
bool glas_ptr_pop(glas*, void**, glas_refct*);


/****************
 * DATA SEALING
 ****************/

/**
 * Sealed values - a dynamic approach to abstract data types
 * 
 * Data is sealed by a key. Currently, keys must refer to registers in 
 * the thread's namespace. To unseal data requires naming the register
 * used to seal it, though the alias used may be different.
 * 
 * The register serves as a source of identity and ephemerality. If the
 * register becomes unreachable and is garbage collected, data sealed by
 * that register may also be garbage collected. Thus, sealed data can be
 * useful for ephemeron tables and similar structures. A register is not
 * modified by its use as a key.
 * 
 * Sealed data may optionally be marked linear, forbidding copy or drop.
 * There may still be many references to linear data, e.g. checkpoints 
 * or concurrent non-deterministic choice. Linear data can be dropped 
 * indirectly, e.g. shoving it into a register then hiding it. But any
 * direct copy or drop - or indirect via containing structure - raises a
 * linearity error. This detects many accidental protocol violations.
 */
void glas_data_seal(glas*, char const* key, bool linear);
void glas_data_unseal(glas*, char const* key);

/*****************************************
 * REGISTERS
 ****************************************/
/** 
 * New, local registers.
 * 
 * This lazily constructs a dense namespace of registers, every suffix
 * of the bound prefix is defined. Registers are initialized to zero.
 */
void glas_ns_reg_locals_bind(glas*, char const* prefix);

/**
 * Runtime-global registers.
 * 
 * The runtime provides one collection of registers shared by all glas
 * threads in the runtime. This serves a similar role as global state.
 */
void glas_ns_reg_globals_bind(glas*, char const* prefix);

/**
 * Associated dictionary of registers.
 * 
 * The glas program model supports a notion of associated state. Between
 * every ordered pair of registers, we can discover more registers. This
 * serves useful roles in abstract and ephemeral state, i.e. essentially
 * we 'unseal' a volume of registers using two registers as keys.
 */
void glas_ns_reg_assoc_bind(glas*, char const* r1, char const* r2, char const* prefix);

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
 * The basic get/set operations.
 * 
 * Get copies data from a register, pushing it on the stack. Set removes
 * data from the stack and writes to register, dropping its prior value.
 * The implicit copy and drop may result in linearity errors, depending
 * on the data.
 */
void glas_reg_get(glas*, char const*); // -- A
void glas_reg_set(glas*, char const*); // A --

/**
 * Linear exchange of data between stack and register.
 * 
 * This is the only primitive operation on registers in the glas program
 * model. Everything else is considered an accelerator.
 */
void glas_reg_xch(glas*, char const*); // A -- A'

/**
 * Register as a queue.
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
 * Every bag operation may non-deterministically reorder the list, or 
 * take and insert items at non-deterministic locations. Bags should be
 * favored over queues where order and determinism are irrelevant.
 * 
 * The advantage of bags is that they support any number of concurrent
 * readers and writers. A runtime can partition the bag then move data 
 * heuristically, to avoid conflict or rebalance loads.
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
 * Virtual Registers
 * 
 * Virtual registers serve as proxies for client resources. Reads and 
 * writes to virtual registers are recorded as metadata for conflict 
 * analysis and reactivity but do not influence observable state.
 * 
 * Like on_commit queues, virtual registers take regular registers as an 
 * identity source by association, but are distinct from them.
 */
void glas_vreg_read(glas*, char const*); // logically read vreg
void glas_vreg_write(glas*, char const*); // logically write vreg
void glas_vreg_rw(glas*, char const*); // logically read and write

/**
 * First-class registers, aka references.
 * 
 * Although glas program model discourages first-class mutable state, it
 * remains convenient for some use cases, and especially for integration
 * of C callback APIs.
 */
void glas_ref_new(glas*); // -- Ref

/**
 * Present reference as a register.
 * 
 * We can use a reference as a register, but this is unidirectional. The
 * glas program model conflicts with references to registers in general.
 */
void glas_ns_reg_ref_def(glas*, char const* name); // Ref --

/**
 * Direct reference access.
 * 
 * Get and set a reference without binding to a name first. This offers
 * small convenience and performance benefits.
 */
void glas_ref_get(glas*); // Ref -- Data
void glas_ref_set(glas*); // Data Ref --
void glas_ref_xch(glas*); // Data Ref -- Data  


/**
 * TBD: Futures and promises.
 * 
 * These can be modeled via references, treating promises as write-once,
 * linear, and futures as read-only, possibly-linear if data is linear.
 * 
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
 * Auxilliary stack for hiding data
 * 
 * The stash serves the role of program primitive %dip, scoping access
 * to a few elements of the data stack. If amt >= 0, transfers amt items
 * from stack to stash, otherwise |amt| is transferred in reverse. The
 * stash is not shared with callbacks or forks.
 * 
 * Items are logically transferred one at a time, though may be batched
 * by the implementation.
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

/**
 * Clear thread-local storage for calling thread.
 * 
 * The glas runtime uses thread-local storage, e.g. for a bump-pointer
 * allocator per OS thread. This API will clear the storage as if the OS
 * thread had exited. The OS thread may freely use the API again, but it 
 * will be treated as a new OS thread by the glas runtime.
 */
void glas_rt_tls_reset();


/**
 * Trigger garbage collection.
 * 
 * This operation will trigger a GC to run ASAP. Flags further influence
 * behavior, e.g. GC will heuristically go back to sleep if there isn't
 * much work available unless flagged to perform a full GC.
 */
typedef enum glas_gc_flags {
    // 0 or bitwise 'or' of the following
    GLAS_GC_FULL = 0b1, // force full GC regardless of state 
} glas_gc_flags;
void glas_rt_gc_trigger(glas_gc_flags);

/*******
 * TBD: STATS
 * 
 * I'd like to provide flexible access to statistics for both a glas 
 * thread and the full runtime. This could include step counts, memory
 * reserved and allocated, allocations aborted, committed and aborted
 * operations counts (perhaps only expensive ops), and many other ad 
 * hoc statistics.
 */




#define GLAS_H
#endif

/**
 *   A glas runtime api.
 * 
 *   Copyright (C) 2025 David Barbour
 *
 *   This program is free software: you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation, either version 3 of the License, or
 *   (at your option) any later version.
 *
 *   This program is distributed in the hope that it will be useful,
 *   but WITHOUT ANY WARRANTY; without even the implied warranty of
 *   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *   GNU General Public License for more details.
 *
 *   You should have received a copy of the GNU General Public License
 *   along with this program. If not, see <https://www.gnu.org/licenses/>.
 */


