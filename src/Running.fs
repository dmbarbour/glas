namespace Glas

/// This module aims to support Glas command-line --run and user-defined verbs.
/// Mostly, this implements an effects handler with:
///
///  network access
///  filesystem access
///  module system access
/// 
/// applications with access to network, filesystem, and transactional memory.
///
/// This should be adequate for evaluating a bootstrap glas command-line utility
/// prior to its compilation to an independent executable. It might also be used
/// for developing some web services early on.
