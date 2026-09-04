# Designing Data-Intensive Applications
**Author:** Martin Kleppmann
**Source:** `06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann---Designing-Data-Intensive-Applications_-O’Reilly-Media-(2017).pdf`
**SHA-256:** `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
**Pages:** 491

## Processing status
`PROCESSED_TO_PROVISIONAL_CANDIDATES` - derived research artifact; nothing here is canonical memory.

## Chapter map
- Chapter 1: Reliable, Scalable and Maintainable Applications (PDF page 1)
- Chapter 2: Data Models and Query Languages (PDF page 25)
- Chapter 3: Storage and Retrieval (PDF page 67)
- Chapter 4: Encoding and Evolution (PDF page 107)
- Chapter 5: Replication (PDF page 145)
- Chapter 6: Partitioning (PDF page 191)
- Chapter 7: Transactions (PDF page 213)
- Chapter 8: The Trouble with Distributed Systems (PDF page 265)
- Chapter 9: Consistency and Consensus (PDF page 311)
- Chapter 10: Batch Processing (PDF page 377)
- Chapter 11: Stream Processing (PDF page 425)

## Candidate knowledge seeds
### book-ddia-2017-c001 - PRINCIPLE
Reliability is a system property, so failure handling must be part of system design rather than treated as an operational afterthought.
- Source locator: `CHAPTER 1`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c002 - PRINCIPLE
Scalability concerns how a system responds to increased load; useful descriptions should state the load parameters and the way performance changes with them.
- Source locator: `CHAPTER 1`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c003 - PRINCIPLE
Data-model choice should follow application access patterns because different models optimize different ways of representing and querying relationships.
- Source locator: `CHAPTER 2`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c004 - PRINCIPLE
Storage-engine design trades write/read paths, indexing structures, compaction, and access patterns rather than optimizing a single operation in isolation.
- Source locator: `CHAPTER 3`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c005 - PRINCIPLE
Encoding formats create compatibility constraints; schema evolution must account for old and new readers/writers operating during deployment transitions.
- Source locator: `CHAPTER 4`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c006 - PRINCIPLE
Replication improves availability and locality but introduces consistency and conflict-resolution questions that the application must understand.
- Source locator: `CHAPTER 5`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c007 - PRINCIPLE
Partitioning distributes data and load, but skewed access patterns can create hotspots even when the dataset is evenly partitioned by size.
- Source locator: `CHAPTER 6`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c008 - PRINCIPLE
Transactions provide useful atomicity and isolation semantics, but stronger guarantees generally require additional coordination or reduce concurrency.
- Source locator: `CHAPTER 7`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c009 - PRINCIPLE
Distributed systems make timing, partial failure, and uncertainty explicit design constraints; assumptions that hold on one machine may fail across a network.
- Source locator: `CHAPTER 8`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c010 - PRINCIPLE
Consistency and consensus are distinct concerns: consistency describes visibility/order guarantees, while consensus addresses agreement among distributed participants.
- Source locator: `CHAPTER 9`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-ddia-2017-c011 - PRINCIPLE
Batch and stream processing are complementary computation models; stream systems reason over unbounded, time-evolving data rather than only finished datasets.
- Source locator: `CHAPTERS 10-11`
- Source SHA-256: `905e1348c2b7955a3e799998ce9b45993b5c8cfa9ef6b93931af7a780b71ac02`
- Verification: `CANDIDATE`; human-gated promotion required.
