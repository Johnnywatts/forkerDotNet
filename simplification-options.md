# Strategic Plan: Align ForkerDotNet with Fundamental 6 Requirements

## Current State Assessment

**What's Been Implemented (Phases 1-3 Complete):**
- ✅ Complete solution structure with Domain/Infrastructure/Service layers
- ✅ Domain core with strong state machines (JobState, TargetCopyState)
- ✅ SQLite persistence layer with WAL-based crash recovery
- ✅ 249 tests passing (143 Domain + 106 Infrastructure, 1 Resilience test timing out)
- ✅ Dual-target configuration system in place
- ✅ SHA-256 hashing with FIPS compliance
- ✅ Full observability framework (metrics, logging, health endpoints)

## Critical Gap Analysis: Fundamental 6 Alignment

**Fundamental 6: "Zero interference of files being moved"**

The current ForkerDotNet implementation has a **fundamental architectural mismatch** with the simplified approach:

**Current Approach (Complex):**
- Files are copied from source → targets while staying in source
- Source cleanup happens after dual-target verification
- Complex state machine tracking partial completion
- Risk of interference during the copy/verify phases

**Simplified Approach Requirements:**
- Input directory must be cleared immediately after successful copy
- NPIC ingestion workflow reads from Primary Clinical folder
- Files are removed from Primary Clinical immediately after NPIC reads them
- Zero interference = no conflicts during any phase

## Strategic Options

### Option 1: Simplified Architecture Redesign (Recommended)
**Redesign ForkerDotNet to match the simpler flow:**

1. **Immediate Move Pattern**:
   - Move files from Input → Primary Clinical (atomic move, not copy)
   - Simultaneously copy to Research folder
   - No complex state tracking - either both operations succeed or rollback

2. **Zero-Interference Implementation**:
   - Use file system moves instead of copies where possible
   - Implement "shadow copying" - copy to temp locations, then atomic rename
   - Eliminate the current complex state machine for simpler success/failure states

3. **Configuration Simplification**:
   - Reduce to: Input → Primary Clinical (move), Input → Research (copy)
   - Remove dual-target verification complexity
   - Focus on atomic operations and immediate cleanup

**Pros:**
- Aligns perfectly with simplified approach requirements
- Eliminates complexity overhead
- Faster implementation and maintenance
- Natural zero-interference pattern

**Cons:**
- Significant rework of existing implementation
- Loss of investment in current state machine complexity
- May need different testing strategy

### Option 2: Commercial Solution Integration
**Leverage proven enterprise tools:**

1. **Evaluate MOVEit Automation** for NHS compliance and atomic dual-destination support
2. **Implement ForkerDotNet as orchestration layer** that coordinates with commercial MFT
3. **Keep domain logic for monitoring/reporting** while delegating actual file operations

**Pros:**
- Proven enterprise-grade reliability
- NHS procurement familiarity
- Built-in governance and auditing
- Immediate atomic dual-destination support

**Cons:**
- Additional licensing costs
- External dependency
- May not fully utilize existing ForkerDotNet investment

### Option 3: Hybrid Approach
**Adapt current ForkerDotNet architecture:**

1. **Simplify state machine** to match simplified workflow
2. **Implement immediate source cleanup** after successful operations
3. **Add "move" operations** alongside existing copy operations
4. **Reduce complexity** while preserving investment in current codebase

**Pros:**
- Preserves existing investment
- Gradual migration path
- Keeps custom control over operations

**Cons:**
- Still carries complexity debt
- May not fully solve interference issues
- Risk of half-measures

## Implementation Steps (Option 1 - Recommended)

1. **Requirements Validation**
   - Confirm simplified approach matches real-world needs
   - Validate that move operations are acceptable for Primary Clinical
   - Ensure Research folder copy requirements are understood

2. **Architecture Simplification**
   - Design new simplified state model (Success/Failed/InProgress)
   - Define atomic move + copy operations
   - Plan immediate source cleanup strategy

3. **Prototype Development**
   - Implement core move/copy operations
   - Demonstrate zero-interference pattern
   - Validate atomic operations work correctly

4. **Performance Testing**
   - Ensure simplified approach meets 1GB/min targets
   - Test with large medical imaging files (20GB+)
   - Validate memory usage stays under 100MB

5. **Migration Strategy**
   - Plan transition from current complex implementation
   - Preserve observability and testing frameworks
   - Maintain NHS security requirements

## Commercial Solutions Reference

From `commercial-solutions.md` analysis, proven alternatives include:

- **Progress MOVEit Automation**: Best balance of simplicity and NHS familiarity
- **Fortra Globalscape EFT**: Strong audit and governance features
- **PeerGFS/Resilio Connect**: Agent-based real-time replication

These solutions naturally handle "atomic across two SMB targets" with temp-file + coordinated rename patterns.

## Recommendation

**Option 1 (Simplified Architecture Redesign)** is recommended because:

1. **Perfect alignment** with Fundamental 6 requirements
2. **Eliminates architectural mismatch** between complex state tracking and simple move operations
3. **Reduces maintenance burden** while preserving core functionality
4. **Faster implementation** than trying to retrofit current complex system
5. **Natural zero-interference** through atomic file operations

The investment in current Domain/Infrastructure layers can be preserved by adapting them to the simpler model rather than complete rewrite.