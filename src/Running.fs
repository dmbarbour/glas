namespace Glas

/// This module supports running of Glas programs as console applications. This
/// is mostly oriented around some effects for network, filesystem, general memory,
/// and so on, adapted for running in a transactional environment. 
module Running =

    type Memory = Map<Value, Value>