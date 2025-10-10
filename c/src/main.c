
#define GLAS_VER "0.1"
#define GLAS_HELP_STR \
  "A pre-bootstrap implementation of the glas CLI\n"\
  "\n"\
  "Environment Vars:\n"\
  "    GLAS_CONF - file path to configuration\n"\
  "       default is ~/.config/glas/conf.glas\n"\
  "\n"\
  "Commands:\n"\
  "    glas --run AppName Arg*\n"\
  "       run application 'env.AppName.app' defined in user config\n"\
  "    glas --script(.FileExt) FilePath Arg*\n"\
  "       run application defined as 'app' after compiling file\n"\
  "       if FileExt is specified, actual file extension ignored\n"\
  "    glas --cmd.FileExt ScriptText Arg*\n"\
  "       equivalent to --script.FileExt with file of given text\n"\
  "    glas --extract BinaryName\n"\
  "       load definition of BinaryName defined in user config\n"\
  "       if this is a binary, print to standard output\n"\
  "    glas --bit TestName*\n"\
  "       run built-in tests. If no TestName, runs all tests.\n"\
  ""

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <glas.h>

static char* my_strdup(char const* s) {
    if(NULL == s) { s = ""; }
    return strcpy((char*)malloc(strlen(s) + 1), s);
}
#define strdup my_strdup

typedef enum glas_act {
    GLAS_ACT_HELP = 0,
    // getting started
    GLAS_ACT_BUILT_IN_TEST,
    GLAS_ACT_EXTRACT_BINARY,
    // getting ambitious
    GLAS_ACT_RUN,
    GLAS_ACT_RUN_SCRIPT,
    GLAS_ACT_RUN_CMD,
    // TBD:
    //   build app without running
    //   binary extraction from config
    //   run apps via script or command
    //   
    GLAS_ACT_UNRECOGNIZED,
} glas_act;

typedef struct {
    glas_act            action;
    char*               app_src;
    char const*         script_lang;
    int                 argc_rem;
    char const* const*  argv_rem;
} glas_cli_options;

void glas_free_cli_options(glas_cli_options* pOpts) {
    free(pOpts->app_src);
    free(pOpts);
}

#if 0
void glas_cli_print_options(glas_cli_options *pOpts) {
    fprintf(stdout, "action=%d\n", pOpts->action);
    fprintf(stdout, "src=%s\n", pOpts->app_src);
    fprintf(stdout, "script_lang=%s\n", pOpts->script_lang);
    fprintf(stdout, "argc_rem=%d\n", pOpts->argc_rem);
    for(int ix = 0; ix < pOpts->argc_rem; ++ix) {
        fprintf(stdout, "arv_rem[%d]=%s\n", ix, pOpts->argv_rem[ix]);
    }
}
#endif

// just brute forcing this for now. Maybe make it elegant later.
glas_cli_options* glas_cli_parse_args(int argc, char const* const* argv) {
    #define CLI_ARG_STEP(N) do { argc -= N; argv += N; } while(0)
    CLI_ARG_STEP(1); // skip executable name
    glas_cli_options* result = (glas_cli_options*) malloc(sizeof(glas_cli_options));
    memset(result, 0, sizeof(glas_cli_options));
    if ((argc < 1) || (0 == strcmp("--help", argv[0]))) { 
        result->action = GLAS_ACT_HELP;
        if(argc >= 1) {
            CLI_ARG_STEP(1);
        }
    } else if(0 == strcmp("--bit", argv[0])) {
        result->action = GLAS_ACT_BUILT_IN_TEST;
        CLI_ARG_STEP(1);
    } else if((0 == strcmp("--extract", argv[0])) && (argc == 2)) {
        result->action = GLAS_ACT_EXTRACT_BINARY;
        result->app_src = strdup(argv[1]);
        CLI_ARG_STEP(2);
    } else if((0 == strcmp("--run", argv[0])) && (argc > 1)) {
        result->action = GLAS_ACT_RUN;
        size_t const buflen = strlen(argv[1]) + 32;
        char buf[buflen];
        if('.' == argv[1][0]) {
            if(0 == argv[1][1]) {
                result->app_src = strdup("app");
            } else {
                snprintf(buf, buflen, "%s.app", argv[1]+1);
                result->app_src = strdup(buf);
            }
        } else {
            snprintf(buf, buflen, "env.%s.app", argv[1]);
            result->app_src = strdup(buf);
        }
        CLI_ARG_STEP(2);
    } else if (argv[0][0] != '-') {
        // syntactic sugar  'opname' => --run 'cli.opname'. 
        //  but I don't want to allocate here.
        result->action = GLAS_ACT_RUN;
        size_t const buflen = 32 + strlen(argv[0]);
        char buf[buflen];
        snprintf(buf, buflen, "env.cli.%s.app", argv[0]);
        result->app_src = strdup(buf);
        CLI_ARG_STEP(1);
    } else if((0 == strncmp("--script.", argv[0], 9)) && (argc > 1)) {
        result->action = GLAS_ACT_RUN_SCRIPT;
        result->script_lang = argv[0] + 9;
        result->app_src = strdup(argv[1]); // alt realpath, but not good for debug
        CLI_ARG_STEP(2);
    } else if((0 == strcmp("--script", argv[0])) && (argc > 1)) {
        result->action = GLAS_ACT_RUN_SCRIPT;
        result->app_src = strdup(argv[1]);
        CLI_ARG_STEP(2);
    } else if((0 == strncmp("--cmd.", argv[0], 6)) && (argc > 1)) {
        result->action = GLAS_ACT_RUN_CMD;
        result->script_lang = argv[0] + 6;
        result->app_src = strdup(argv[1]);
        CLI_ARG_STEP(2);
    } else {
        result->action = GLAS_ACT_UNRECOGNIZED;
    }
    result->argc_rem = argc;
    result->argv_rem = argv;
    #undef CLI_ARG_STEP
    return result;
}

int main(int argc, char const* const* argv) 
{
    glas_cli_options* pOpt = glas_cli_parse_args(argc, argv);
    //glas_cli_print_options(pOpt);


    if(GLAS_ACT_HELP == pOpt->action) {
        fprintf(stdout, "glas version %s\n", GLAS_VER);
        fprintf(stdout, "%s", GLAS_HELP_STR);
        fflush(stdout);
        return 0;
    } 

    glas* g = glas_thread_new();

    #if 0

    if(!glas_apply_user_config(grt)) {
        fprintf(stderr, "Unable to load configuration.\n");
        glas_destroy(grt);
        return -1;
    }
    
    if(GLAS_ACTION_RUN == opt.action) {
        size_t const full_src_len = strlen(opt.app_src) + 32;
        char full_name[full_src_len];
        snprintf(full_name, full_src_len, "env.%s.app", opt.app_src);
        glas_run(grt, full_name, opt.argc_app, opt.argv_app);
    } else if(GLAS_ACTION_RUN_CLI_APP == opt.action) {
        size_t const full_src_len = strlen(opt.app_src) + 32;
        char full_name[full_src_len];
        snprintf(full_name, full_src_len, "env.cli.%s.app", opt.app_src);
        glas_run(grt, full_name, opt.argc_app, opt.argv_app);
    } else {
        glas_destroy(grt);
        return -1;
    }
    #endif

    glas_thread_exit(g);
    glas_free_cli_options(pOpt);
    return 0;


}
