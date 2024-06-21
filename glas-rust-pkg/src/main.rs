///
// This project is a bootstrap implementation of the glas language system in Rust.
//
// Why Rust? Both to learn Rust and to see if I can get adequate performance from
// various interpreters in Rust. 
// 

fn help_text() -> String {
    vec!["The glas command line interface (CLI) is primarily executed as:"
        ,""
        ,"    glas opname Arg1 Arg2 ..."
        ,"        # shorthand for"
        ,"    glas --run cli.opname Arg1 Arg2 ..."
        ,""
        ,"Other run modes include:"
        ,""
        ,"    glas --script.FileExt (FileName) Arg1 Arg2 ..."
        ,"        # intended for use with shebang, ignoring actual file extension"
        ,"    #!/usr/bin/glas --script.FileExt"
        ,""
        ,"    glas --cmd.FileExt (ScriptText) Arg1 Arg2 ..."
        ,"        # interprets text as a file with given extension"
        ,""
        ,"There are also a few modes that won't attempt to run an application:"
        ,""
        ,"    glas --help           # print this text"
        ,"    glas --version        # print version info"
        ,"    glas --check Module   # try to compile module"
        ,"    glas --init           # create the config file"
        ,""
        ,"Configuration is primarily via file instead of the command line."
        ,"The selected configuration is specified by GLAS_CONF environment"
        ,"variable, falling back to a reasonable default:"
        ,""
        ,"    ~/.config/glas/default.conf           # Linux"
        ,"    %AppData%\\glas\\default.cfg            # Windows"
        ,""
        ].into_iter().map(|s| s.to_string()).collect::<Vec<String>>().join("\n")
}

//
//      glas --help                 # print help description
//      glas --version              # print implementation version
//      glas --check ModuleName*    # try to compile the module(s)
//

type ModuleName = String;
type ScriptFile = String;
type CommandText = String;
type FileExt = String;
type ForwardArgs = Vec<String>;

#[derive(Debug)]
enum Mode {
    Run(ModuleName, ForwardArgs),
    Script(FileExt, ScriptFile, ForwardArgs),
    Cmd(FileExt, CommandText, ForwardArgs),
    Check(ModuleName), 
    Init,
    Version,
    Help,
    Unrecognized
}

fn parse_args(args: Vec<String>) -> Mode {
    // Rust doesn't make it easy to match on a vector of strings,
    // so I ended up just using if/then.
    if (args.len() >= 1) && !(args[0].as_str().starts_with("-")) {
        let opname = format!("glas-cli-{}", args[0]);
        let rem = Vec::from_iter(args[1..].into_iter().cloned());
        Mode::Run(opname, rem)
    } else if (args.len() >= 2) && (args[0].as_str() == "--run") {
        let opname = args[1].to_string();
        let rem = Vec::from_iter(args[2..].into_iter().cloned());
        Mode::Run(opname, rem)
    } else if (args.len() >= 2) && (args[0].as_str().starts_with("--script")) {
        let file_ext = args[0][8..].to_string();
        let script_file = args[1].to_string();
        let rem = Vec::from_iter(args[2..].into_iter().cloned());
        Mode::Script(file_ext, script_file, rem)
    } else if (args.len() >= 2) && (args[0].as_str().starts_with("--cmd")) {
        let file_ext = args[0][5..].to_string();
        let script_text = args[1].to_string();
        let rem = Vec::from_iter(args[2..].into_iter().cloned());
        Mode::Cmd(file_ext, script_text, rem)
    } else if (args.len() == 2) && (args[0].as_str() == "--check") {
        Mode::Check(args[1].to_string())
    } else if (args.len() == 1) && (args[0].as_str() == "--init") {
        Mode::Init
    } else if (args.len() == 1) && (args[0].as_str() == "--help") {
        Mode::Help
    } else if (args.len() == 1) && (args[0].as_str() == "--version") {
        Mode::Version
    } else {
        Mode::Unrecognized
    }
}

fn run_glas(operation : Mode) {
    println!("Run Mode: {:?}", operation);
    match operation {
        Mode::Run(_m, _args) => (),
        Mode::Script(_lang, _file, _args) => (),
        Mode::Cmd(_lang, _script, _args) => (),
        Mode::Check(_m) => (),
        Mode::Init => (),
        Mode::Version => (),
        Mode::Help => 
            println!("{}", help_text()),
        Mode::Unrecognized => {
            println!("Unrecognized arguments!");
            run_glas(Mode::Help)
        }
    }
}

fn main() {
    let args = std::env::args().skip(1).collect(); // skip executable name
    let mode = parse_args(args);
    run_glas(mode);
}
