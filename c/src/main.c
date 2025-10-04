
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
  ""

#include <glas.h>
#include <stdio.h>
#include <string.h>
#include <stdbool.h>

enum glas_action {
    GLAS_ACTION_HELP = 0,
    GLAS_ACTION_RUN,
    GLAS_ACTION_RUN_CLI_APP,
    // TBD:
    //   build app without running
    //   binary extraction from config
    //   run apps via script or command
    //   
};

typedef struct {
    enum glas_action        action;
    char const*             app_src;
    char const*             script_lang;
    int                     argc_app;
    char const* const*      argv_app;
} glas_cli_options;





glas_cli_options 
glas_cli_parse_args(int argc, char const* const* argv) {
    // this is a bit simplistic at the moment. 
    glas_cli_options result = { 0 };
    if (argc < 1) { 
        // nop
    } else if((0 == strcmp("--run", argv[0])) && (argc > 1)) {
        result.action = GLAS_ACTION_RUN;
        result.app_src = argv[1];
        result.argc_app = argc - 2;
        result.argv_app = argv + 2;
    } else if (argv[0][0] != '-') {
        // syntactic sugar  'opname' => --run 'cli.opname'. 
        //  but I don't want to allocate here.
        result.action = GLAS_ACTION_RUN_CLI_APP;
        result.app_src = argv[0];
        result.argc_app = argc - 1;
        result.argv_app = argv + 1;
    } 

    #if 0
    else if((0 == strncmp("--script", argv[0], 8)) && (argc > 1)) {
        result.action = GLAS_ACTION_SCRIPT;
        result.app_src = argv[1];
        result.script_lang = (0 != argv[0][8]) ? argv[0] + 8 : NULL;
        result.argc_app = argc - 2;
        result.argv_app = argv + 2;
    } else if((0 == strncmp("--cmd.", argv[0], 6)) && (argc > 1)) {
        result.action = GLAS_ACTION_CMD;
        result.app_src = argv[1];
        result.script_lang = argv[0] + 5;
        result.argc_app = argc - 2;
        result.argv_app = argv + 2;
    }
    #endif
    return result;
}


int main(int argc, char const* const* argv) 
{
    glas_cli_options opt = glas_cli_parse_args(argc, argv);
    if(GLAS_ACTION_HELP == opt.action) {
        fprintf(stdout, "glas version %s\n", GLAS_VER);
        fprintf(stdout, "%s", GLAS_HELP_STR);
        return 0;
    } 

    glas_client gcx = { stdin, stdout, stderr };
    glas_rt* grt = glas_create(&gcx);
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

    glas_destroy(grt);
    return 0;


}
