namespace Glas

// idea: maybe implement a map that hashes its keys first
// e.g. something like this:
type HashMap<'K,'V when 'K : comparison> = Map<struct(int * 'K), 'V>

// but this is a minor performance improvement only when keys share a
// lot of structure such that direct comparisons take too long. Will
// defer for now.
