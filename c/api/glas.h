/**
 * An API for the glas runtime.
 */
#pragma once
#ifndef GLAS_H
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

// forward declarations
typedef struct glas glas;
typedef struct glas_ns_tl glas_ns_tl;
typedef struct glas_refct glas_refct;
typedef struct glas_file_ref glas_file_ref;

/*****************
 * GLAS THREADS
 *****************/
/**
 * Create a fresh glas thread.
 * 
 * A glas thread consists of data stack, stash, and namespace, initially
 * empty. The client can transfer data to and from the stack, update the
 * namespace, call defined programs.
 * 
 * A glas thread is NOT mt-safe. However, it may migrate freely between 
 * OS threads. The limitation is using it from one OS thread at a time.
 * 
 * Error handling is transactional. After an error occurs, further steps
 * are best-effort and may accumulate more errors. But the thread cannot
 * commit with errors, only revert.
 */
glas* glas_thread_new();

/**
 * Load a configuration file.
 * 
 * This relies on a built-in front-end ".glas" compiler. If the compiler
 * is redefined within the configuration, we attempt bootstrap. Primary
 * outputs from a configuration are ad hoc runtime options in "glas.*"
 * and the "%*" names (especially "%env.*").
 * 
 * If the configuration file is NULL, we search fallback locations: 
 * 
 * - GLAS_CONF environment variable     if defined
 * - ${HOME}/.config/glas/conf.glas     on Linux
 * - %AppData%\glas\conf.glas           on Windows (if ported)
 * 
 * If there is no error, the configuration's definitions are loaded 
 * under a specified prefix, default "conf." if dst is NULL.
 */
void glas_load_conf(glas*, char const* dst, glas_file_ref const*);

/**
 * Load a script file.
 * 
 * Where a configuration specifies an environment, a script assumes one.
 * The specified 'base' is passed directly as script base. Usually, this
 * consists of '%*' outputs from a configuration, such that we can treat
 * a script as being imported at the bottom of the configuration file.
 * 
 * The main output from a script is (usually) a definition of 'app', an
 * application. Though, scripts could just be used to load definitions.
 * 
 *   Default 'base': {{"%", "conf.%"}, {"", NULL}, {NULL, NULL}}
 *   Default 'dst': "script."
 */
void glas_load_script(glas*, char const* dst, 
    glas_file_ref const*, glas_ns_tl* const base);

/**
 * Fork a glas thread.
 * 
 * This can be useful for task-based concurrency. The fork receives a
 * copy of the origin's current namespace and optionally a transfer of
 * a few data stack elements. 
 * 
 * A fork begins in an 'unstable' state, meaning that it may receive a
 * CANCELED error in the future, e.g. if origin aborts and backtracks.
 */
glas* glas_thread_fork(glas*, uint8_t stack_transfer);

/**
 * Test stability of a glas thread.
 * 
 * A glas thread is unstable if it may later observe the CANCELED error.
 * This is possible due to aborting an operation that creates a thread.
 * Stability is naturally monotonic: once stable, always stable.
 * 
 * This operation returns true if the thread is stable, false otherwise.
 */
bool glas_thread_is_stable(glas*);

/**
 * Terminate a glas thread.
 * 
 * Exit when finished with a thread returned by new, fork, or clone.
 * 
 * This tells the runtime that there will be no more operations. Any
 * pending operations since last commit are aborted. If the thread is
 * unstable, prior operations may be pending stabilization. Those are
 * still applied. Memory is recycled when possible.
 * 
 * For threads bound to callbacks, do not use exit. Those belong to the
 * runtime and will be managed after returning from the callback.
 */
void glas_thread_exit(glas*);

/**
 * Set debug name for a thread. Appears in debug messages.
 */
void glas_thread_set_debug_name(glas*, char const* debug_name);

/***************************
 * MEMORY MANAGEMENT
 **************************/
/**
 * Reference-counting shared objects.
 *
 * Used for zero-copy binaries, foreign pointers, and callbacks. The 
 * reference count may be separated from the data pointer, e.g. in case
 * of slicing a large binary.
 * 
 * Reference counts should be pre-incremented, such that the recipient
 * needs only perform decref. If no management is needed, simply set the
 * refct_upd function pointer to NULL.
 */
struct glas_refct {
    void (*refct_upd)(void* refct_obj, bool incref);
    void  *refct_obj;
};

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
 * Prefix-to-prefix Translations.
 * 
 * The glas namespace model uses prefix-to-prefix translations for many
 * purposes. Within C, we'll simply represent these as an array of pairs
 * of strings terminating with NULL in lhs. Example:
 * 
 *   { {"bar.", "foo."}, { "$", NULL }, { NULL, NULL } }
 * 
 * During lookup, the runtime will find the longest matching lhs prefix
 * then rewrite to rhs. If rhs is NULL, name is treated as undefined.
 * 
 * We logically extend names with an infinite suffix of "." characters.
 * Thus, we can translate "bar." to translate both the name "bar" and 
 * all "bar.*" components, or "bar.." for just name "bar" (assuming we
 * avoid ".." sequences within names).
 */
struct glas_ns_tl { char const *lhs, *rhs; };

// TODO: separate eval and bind/def of namespace ASTs.

/**
 * Apply translation to thread's namespace.
 * 
 * This affects future operations on the glas thread. Definitions that
 * become unreachable may be garbage collected after step commit.
 */
void glas_ns_tl_apply(glas*, glas_ns_tl const*);

/**
 * Scoped Namespaces (via convention)
 * 
 * These are modeled as namespace translations:
 * 
 *   push - {{ "^", "" }, {NULL, NULL}}
 *   pop  - {{ "", "^" }, {NULL, NULL}}
 * 
 * That is, after 'push' the name 'foo' is still available as 'foo' but
 * also as '^foo', but a prior '^foo' is now only available as '^^foo'.
 * Upon 'pop', the name 'foo' now refers to '^foo'.
 */
void glas_ns_scope_push(glas*);
void glas_ns_scope_pop(glas*);

/**
 * Namespace AST evaluation.
 * 
 * This expects a valid namespace AST on the data stack. Evaluates in
 * the current namespace, resulting in an abstract namespace term on
 * the data stack, modulo error. The usual step after evaluation is to
 * define or bind the term.
 */





/**
 * Push a representation of a translation onto the data stack
 */
void glas_ns_tl_push(glas*, glas_ns_tl const*); // -- TL


/**
 * Utility. Hide names or prefixes from current scope.
 * These are lightweight wrappers around glas_ns_tl_apply.
 */
void glas_ns_hide_def(glas*, char const* name);
void glas_ns_hide_prefix(glas*, char const* prefix);

/**
 * Creates a "data" definition.
 * 
 * Pops data from stack. Wraps as "data"-tagged d:Data then assigns to
 * indicated name. Causes linearity error if the data is linear.
 */
void glas_ns_data_def(glas*, char const* name);

/** 
 * Define by evaluation.
 * 
 * Pop a namespace AST from the data stack. Evaluates in context of the
 * current environment, optionally translated by eval_env. 
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
void glas_ns_ast_as_closed_term(glas*); // AST -- AST (closed)

/*************************
 * PROGRAMS
 *************************/

/**
 * Define programs using callbacks.
 * 
 * The callback receives arguments and returns results via data stack.
 * Stack arity is specified ahead of time via ar_in and ar_out. A glas
 * thread is introduced as a scratch space.
 * 
 * In most cases, a callback is performed from a transactional context.
 * The thread is unstable until the hosting transaction commits. Use the
 * on-commit and on-abort mechanisms to defer 'unsafe' effects.
 * 
 * Note: This is modeled as a "prog" callback, so there is no access to 
 * the caller's environment. 
 */
typedef struct glas_prog_cb {
    bool (*cb)(glas*, void* client_arg);
    void* client_arg;           // opaque, passed to operation
    glas_refct refct;           // integrate GC for client_arg
    uint8_t ar_in, ar_out;      // data stack arity (enforced!)
    char const* debug_name;     // appears in stack traces, etc.
    // for extensibility, unset fields should be zeroed
} glas_prog_cb; 

void glas_ns_cb_def(glas*, char const* name, glas_prog_cb const*, glas_ns_tl const*);

// TBD: constructors for %do, %cond, etc.. 

/*********************************
 * BULK DEFINITION AND MODULARITY
 *********************************/

/**
 * Access built-in primitive definitions.
 * 
 * Provides primitive definitions under a given prefix (default "%"). 
 * This includes program constructors, annotations, and accelerators.
 * An exception is built-in front-end compilers for bootstrapping.
 */
void glas_load_primitives(glas*, char const* prefix);

/**
 * Access built-in front-end compilers.
 */

// TBD: load a file as a module or AST value


/*****************
 * CONFIGURATION
 *****************/
/**
 * Flexible 'file' references.
 * 
 * - src: a file path, or file content if 'embedded'
 * - lang: NULL or file extension (override or embedded)
 * - embedded: if true, interprets src as file content 
 * 
 * The front-end compiler is selected based on 'lang' or file extension
 * if NULL. A built-in compiler exists for ".glas" and ".glob" files, 
 * but users can override or introduce new compilers at '%env.lang.Ext'.
 * 
 * If 'embedded' is true, 'lang' must be set.
 */
struct glas_file_ref {
    char const* src;
    char const* lang;
    bool embedded;
} glas_file_ref;





/**************************
 * CALLING FUNCTIONS
 **************************/
/**
 * Call a defined program by name.
 * 
 * The runtime expects user definitions to be tagged. Recognized tags:
 * 
 * - "prog" for a program definition, runtime verifies then runs
 * - "data" for embedded data; call pushes to data stack (%data)
 * - "call" for `Object -> Def`, returning "prog" or "data" Def.
 * 
 * In case of "call" we'll provide the caller's 'env' (translated). Any
 * more than that will require defining a call adapter manually.
 */
void glas_call(glas*, char const* name, glas_ns_tl const* caller_env, bool* commits);



/**
 * Ask runtime to prepare definitions.
 * 
 * This operation is intended to mitigate lazy loading of very large
 * namespaces, asking runtime worker threads to load in the background.
 */
void glas_call_prep(glas*, char const* name);

/*********************************
 * RUNNING APPLICATIONS
 *********************************/

/**
 * TBD: Run an application.
 * 
 * Applications are run asynchronously, and they don't start until the
 * caller commits the request and stabilizes. Instead, running the app
 * immediately returns an abstract reference to interact with, wait on, 
 * or kill the app.
 */

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
void glas_checkpoints_clear(glas*); // drop all checkpoints

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
 * as a copy. But a linear pointer must be dropped via glas_ptr_pop.
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
 * References, or first-class registers. (Tentative.)
 * 
 * The glas program model does not have built-in support for first-class
 * registers, but they may be provided through an effects API. There are
 * two primary operations: constructing a new reference, and binding the
 * reference to a named register in the current scope.
 * 
 * Note that references are non-linear even if the data they contain is
 * linear. This easily results in linearity leaks, dropping data early.
 */
#if 0 
void glas_ref_new(glas*); // Data -- Ref ; runtime-ephemeral
void glas_ref_db_new(glas*); // Data -- Ref ; database-ephemeral
void glas_ns_reg_ref(glas*, char const* name); // Ref --
#endif 

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
 * to a few elements of the data stack. If amt > 0, transfers amt items
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
 *   "abcd-abcab"   drops d from top of stack, copies ab to top of stack
 * 
 * This operation navigates to '-', scans leftwards, popping items into
 * local variables [a-zA-Z]. Then it scans rightwards from '-', pushing 
 * variables back onto the stack. It's an error if the string reuses a
 * variable on the left, refers to unassigned variables on the right, or
 * lacks the '-' separator. Copy or drop of linear data is detected.
 * 
 * Note: An invalid moves string may cause the process to abort.
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
 * 
 * Returns 'false' if at least one test fails. Also prints progress and
 * pass/fail information to standard output.
 */
bool glas_rt_run_builtin_tests();

/**
 * Clear thread-local storage for calling thread.
 * 
 * A few resources, especially memory allocation and GC integration, are
 * per OS-level thread for performance reasons. A 'glas*' thread safely
 * migrates between OS threads if used by only one OS thread at a time.
 * 
 * By default, the thread-local storage per OS thread remains until that
 * OS thread exits. This operation does the same, early. Further use of 
 * the glas API will be treated as a new OS thread.
 * 
 * A potential motive is to suppress complaints about memory usage when 
 * instrumenting C code.
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
 *   Copyright (C) 2025, 2026 David Barbour
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


