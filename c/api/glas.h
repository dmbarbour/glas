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

/**
 * Reference to an opaque glas context.
 * 
 * Each glas context logically consists of:
 * - a coroutine
 * - a data stack (and auxilliary stash)
 * - a lexically scoped namespace
 * 
 * Through this API, the client issues commands for that coroutine. 
 * This API is synchronous, so the caller waits for each operation
 * to complete. 
 */
typedef struct glas glas;

/**
 * Create a new glas context.
 * 
 * This starts with an empty namespace, but users may load definitions,
 * registers, and callbacks. 
 * 
 * The data stack and stash are logically infinite, filled with zeroes.
 * Thus, there is no risk of stack underflow.
 */
glas* glas_create();

/**
 * Fork a context. The returned context shares the namespace but
 * starts with an empty data stack. Consider use of xchg to move
 * some data to the fork, if the registers are inadequate.
 * 
 * Caveat: glas has fork-join semantics for coroutines, and this
 * is inherited by glas* contexts. If forked within a transaction, 
 * it must be destroyed for the transaction to commit. If forked 
 * in a callback, must be destroyed for the caller to proceed.
 * 
 * However, at the toplevel, there is nothing to 'join', thus 
 * holding a fork doesn't block anything else from proceeding.
 */
glas* glas_fork(glas*);

/**
 * Destroy a glas context.
 * 
 * This is for contexts obtained via glas_create or glas_fork APIs.
 * Those obtained via callbacks are implicitly destroyed on return.
 */
void glas_destroy(glas*);

/**
 * Stack exchange. 
 * 
 * Move amt data elements from src to dst, preserving order.
 * Or, if amt is negative, moves from dst to src instead.
 */
void glas_data_xchg(glas* src, int amt, glas* dst);

/**
 * Visualized data shuffling. 
 *   
 *   "abc-abcabc"   copy3
 *   "abc-b"        drops a and c
 *   "abcd-abcab"   drops d, copies ab to top of stack
 * 
 * This operation will read stack items into temporary variables based
 * on the LHS of '-' (each character in a-z may be assigned exactly once), 
 * then will write data back onto the stack based on the RHS. The rightmost
 * var of LHS or RHS represents 'top' of data stack.
 * 
 * This operation fails, returning false, if it would copy or drop linear
 * data unless the 'force' parameter is true. In case of malformed exchange
 * string, it instead halts the process.
 * 
 * Note: If you're using this operation frequently, you should consider
 * introducing registers and simplifying your stack!
 */
_Bool glas_data_move(glas*, char const* exchange_str, _Bool force_linear);

/**
 * Specialized copy and drop. Copies an amount of items. 
 * Error if amt < 0.
 * 
 * Like shuffle, these operations may fail with linear data unless
 * forced.
 */
_Bool glas_data_copy(glas*, size_t amt, _Bool force_copy_linear);
_Bool glas_data_drop(glas*, size_t amt, _Bool force_drop_linear);

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
 * Copies data from binary at top of stack into client buffer.
 * 
 * Returns number of bytes read, which may be less than max if
 * the binary is shorter or  
 * 
 * If buf is NULL, instead returns number of bytes that could be
 * read if buf was not NULL (up to max). This may be useful for
 * deciding allocations, at the cost of two passes.
 * 
 * Does not modify the data stack.
 */
size_t glas_binary_peek(glas*, void* buf, size_t max);

/** 
 * Push a binary to the data stack. 
 */
void glas_binary_push(glas*, void const* buf, size_t ct);

/**
 * Read an integer from top of data stack.
 * 
 * Fails if target is not an integer or outside range.
 */
_Bool glas_integer_peek(glas*, int64_t*);

/**
 * Write an integer to top of data stack.
 */
void glas_integer_push(glas*, int64_t);

/** TBD
 * - operations to build data (sum, append, split, etc.)
 * - interaction with transaction
 *   - query whether a transaction is still viable
 *   - callback hooks (precommit, commit, abort)
 * - access to bgcalls
 * - access to other reflection APIs
 *   - logging
 *   - error info
 *   - etc.
 * - invoking operations through name on stack
 * - support for translations (array of structs of C strings?)
 */


#if 0

/**
 * Attempt to load configuration from default locations.
 * 
 *   ${GLAS_CONF} environment variable  (if defined)
 *   ${HOME}/.config/glas/conf.glas     (on Linux)
 *   %AppData%\glas\conf.glas           (in Windows, eventually)
 */
_Bool glas_apply_user_config(glas_rt*);
// TBD: configure from specified file or configuration text

/**
 * Load and run an application defined in the configuration.
 */
_Bool glas_run(glas_rt*, char const* appname, 
    int argc, char const* const* argv);

#endif


/**
 * To support flexible interactions, the client may 'define'
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
 * Returning 'false' will represent error or divergence.
 */
typedef _Bool (*glas_def_cb)(void* , glas*);

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
    glas_def_cb, void*, char const* caller_prefix);


/**
 * Check whether a specific name is defined in context of the
 * given context. 
 * 
 * Caveat: this may trigger a lot of lazy namespace computation,
 * so it should only be checked if there is a need to do so.
 * 
 * Note: This always returns true for register names. All are
 * defined, even if they're not in use yet.
 */ 
_Bool glas_name_defined(glas*, char const* name);

/**
 * Check whether a at least one name with the given prefix 
 * is defined. Same caveats and notes as name_is_defined. 
 */
_Bool glas_prefix_inuse(glas*, char const* prefix);


/**
 * hook for transaction callbacks?
 */

/**
 * This returns true if the context is inside an atomic transaction,
 * whether client-generated or via callback, otherwise false.
 */
_Bool glas_context_is_atomic(glas*);


/**
 * Clients may initiate transactions. Conceptually, each 'glas*' context
 * may support a stack of hierarchical transactions. 
 */



#define GLAS_H
#endif


