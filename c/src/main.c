/**
 *   An implementation of the glas command-line interface.
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
        size_t const buflen = strlen(argv[1]) + 32;
        char buf[buflen];
        snprintf(buf, buflen, "env.%s", argv[1]);
        result->app_src = strdup(buf);
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

int glas_cli_bit(int argc, char const* const* argv);
int glas_cli_extract(char const* src);

int main(int argc, char const* const* argv) 
{
    int result = 0;
    glas_cli_options* pOpt = glas_cli_parse_args(argc, argv);
    //glas_cli_print_options(pOpt);

    if((GLAS_ACT_HELP == pOpt->action) || 
       (GLAS_ACT_UNRECOGNIZED == pOpt->action)) 
    {
        fprintf(stdout, "glas version %s\n", GLAS_VER);
        fprintf(stdout, "%s", GLAS_HELP_STR);
    } else if(GLAS_ACT_BUILT_IN_TEST == pOpt->action) {
        result = glas_cli_bit(pOpt->argc_rem, pOpt->argv_rem);
    } else if(GLAS_ACT_EXTRACT_BINARY == pOpt->action) {
        result = glas_cli_extract(pOpt->app_src);
    } else {
        fprintf(stdout, "command not yet supported!\n");
    }


    #if 0

    
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

    fflush(stdout);
    glas_free_cli_options(pOpt);
    return result;
}


int glas_cli_extract(char const* src) {
    (void) src;
    glas* g = glas_thread_new();
    

    glas_thread_exit(g);
    return -1;
}

//#include "minunit.h"


int glas_cli_bit(int argc, char const* const* argv) {
    (void) argc; (void) argv;
    int tests_failed = 0;
    if(!glas_rt_run_builtin_tests()) {
        ++tests_failed;
        fprintf(stdout, "glas runtime built-in tests failed\n");
    }
    return tests_failed;
}

