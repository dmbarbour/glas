# Glas Distribution Filesystem (GlaDFS)

Glas distributions eventually require a suitable filesystem. This is a relatively low priority while Glas is young. But it seems a useful project by its own merits, so I'll write my ideas down.

## Desiderata

* decentralized storage
* incremental download
* storage provider independence
* efficient merges and diffs
* safe atomic updates
* snapshots and history
* structure sharing
* security by default

Glas doesn't require conventional access-control security. But it does need to be secure against a distrusted storage provide or outside attacker.

## Proposal

Leverage ideas of log-structured merge trees, merkle trees, and radix trees to construct an immutable, persistent structure with cheap snapshots that be shared within a network. Nodes will be encrypted and accessed by secure hash of the encryption key.

Build a filesystem above this structure, i.e. by writing a server that supports the FUSE/WinFSP interfaces and suitable network interfaces for incremental download and sharing.

## Representation

Start at a tree node. Each tree node has:

* extended shared prefix 
* optional node reference and keycount
* optional deletion, definition, or update
* sparse array mapping bytes to children

To represent the options compactly, we could use a header byte to represent a bitfield, with update = delete + define. 

The shared prefix and definition will be encoded as fragmented binaries, an array of fragments. Each fragment is:

* sized binary inline
* reference to a raw binary, with size
* reference to a fragmented binary, with total size

This supports representation of finger trees and ropes, and inline binaries for smaller definitions. The recorded size supports efficient indexed access.

A node reference enables the trie to be encoded across multiple fragments. In a search, we must check the last node reference in the search path if a symbol wasn't mentioned in the current node.

The sparse array will often be empty or just a few items. So, I think that it's best to simply represent it as a sequence of (byte, child) pairs. However, encoding the child inline would hinder efficient query on the node - we'd need to scan through all the irrelevant child nodes. 

So we should use a `(byte, child offset)` instead. Layout in the binary should use a depth-first encoding. A depth-first encoding will minimize the average offset, and simplify extraction of subnodes during encoding.

Offsets can be encoded as a count of bytes after the offset. Thus, `0` is the next byte. We don't need negative offsets because we're encoding a tree structure, which simplifies things. All sizes and offsets can use a simple var-nat encoding - 7 bits data per byte, all except final have `1` in the high bit. 

## Security

We can feasibly secure this model by encrypting remote storage and using a secure hash of the encryption key as the lookup key. The encryption might include a secure hash, so we can further authenticate the data, but it should also include some randomized salt to prevent data prediction attacks. Structure sharing, then, would require a shared history of updates. 

## Garbage Collection

References will need to be garbage collected. This is quite a challenge in context of encrypted, distributed storage. Tahoe-LAFS has a similar problem, using timing in its model[1](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/garbage-collection.html).

We can assume that a distribution is stored fully, somewhere. So what we're really garbage collecting are old binaries that we downloaded but are no longer relevant. Importantly, if they do become relevant again, they may be re-downloaded from the distribution in question.

A content distribution network can reduce the burden on this server.
