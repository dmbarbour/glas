namespace Glas

module TransactionalMemory = 

    // My goal with this module is to support a generic transactional memory, preferably
    // a multi-thread safe memory. Simultaneously, this must support lazy or promised 
    // memory, i.e. the ability to trigger background activity to load memory from a file
    // stream.
    //
    // This memory will provide a substrate for encoding effects. It is also feasible to 
    // directly target transactional memory instead of a data stack when compiling code.
    // 
