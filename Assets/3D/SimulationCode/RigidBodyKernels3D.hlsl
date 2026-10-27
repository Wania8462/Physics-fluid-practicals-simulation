// Rigid body kernels, included at the end of Simulation.compute, which declares every buffer, uniform and
// kernel pragma used here (ComputeHelper reflection only reads that file). Math helpers: RigidBodyMath3D.hlsl

// A reduction slot of SumCouplingForces holds a force and a torque
#define CouplingSlotSize 2

// SumContacts groups contacts per wall axis (x, y, z) plus one group for all other bodies. For each group a
// reduction slot holds the push out (w = number of contacts), the friction, the torque and the approach
#define ContactGroups 4
#define ContactSlotSize 16

// ---------- Boundary force buffers: atomic fixed-point accumulators, like the fluid ones ----------

void SetBoundaryForceBuffer(uint index, float4 value)
{
    BoundaryForceBuffersX[index] = (int)(value.x * FloatScale);
    BoundaryForceBuffersY[index] = (int)(value.y * FloatScale);
    BoundaryForceBuffersZ[index] = (int)(value.z * FloatScale);
}

void AtomicAddBoundaryForceBuffer(uint index, float4 value)
{
    InterlockedAdd(BoundaryForceBuffersX[index], (int)(value.x * FloatScale));
    InterlockedAdd(BoundaryForceBuffersY[index], (int)(value.y * FloatScale));
    InterlockedAdd(BoundaryForceBuffersZ[index], (int)(value.z * FloatScale));
}

float3 ReadBoundaryForceBuffer(uint index)
{
    return float3((float)BoundaryForceBuffersX[index],
                  (float)BoundaryForceBuffersY[index],
                  (float)BoundaryForceBuffersZ[index]) / FloatScale;
}

// ---------- Fluid-body coupling, called from DoubleDensityRelaxation and ApplyViscosity ----------

// Density the boundary particles add to a fluid particle, each weighted by psi = restDensity * volume (port of 2D)
void AddBoundaryDensity(float4 position, uint3 cellPosition, inout float density, inout float nearDensity)
{
    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset; // negatives wrap and fail the bounds check
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStartRO[cell]; j < BoundaryCellStartRO[cell + 1]; j++)
        {
            uint boundary = BoundarySortedIndicesRO[j];
            float magSq = DistanceSq(position, BoundaryPositionsRO[boundary]);
            float q = sqrt(magSq) / interactionRadius;

            // Branchless mask: 0 when the particles coincide or are out of range
            float w = step(1e-12, magSq) * step(q, 1.0);
            float psi = restDensity * BoundaryVolumesRO[boundary];

            density += w * psi * QuadraticSpikyKernel(q);
            nearDensity += w * psi * CubicSpikyKernel(q);
        }
    }
}

// Boundary pressure pushes the fluid particle away and stores the reaction on the boundary particle, which
// SumCouplingForces turns into force and torque on its body (port of 2D). Pressure is clamped at 0 so bodies
// never pull fluid in
void ApplyBoundaryPressure(uint index, float4 position, uint3 cellPosition, float pressure, float nearPressure)
{
    float boundaryPressure = max(pressure, 0.0);
    float boundaryNearPressure = max(nearPressure, 0.0);

    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset;
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStartRO[cell]; j < BoundaryCellStartRO[cell + 1]; j++)
        {
            uint boundary = BoundarySortedIndicesRO[j];
            float4 boundaryPosition = BoundaryPositionsRO[boundary];
            float magSq = DistanceSq(position, boundaryPosition);
            float mag = sqrt(magSq);
            float q = mag / interactionRadius;

            // Branchless mask: 0 when the particles coincide or are out of range
            float w = step(1e-12, magSq) * step(q, 1.0);
            float4 r = (boundaryPosition - position) / max(mag, 1e-9);
            float4 displacement = w * restDensity * BoundaryVolumesRO[boundary] *
                PressureDisplacement(dt, q, boundaryPressure, boundaryNearPressure, r);

            AtomicAddForceBuffer(index, -displacement);
            AtomicAddBoundaryForceBuffer(boundary, displacement);
        }
    }
}

// Friction from the boundary particles slows the fluid particle only, as in 2D
void ApplyBoundaryFriction(uint index, float4 position, float4 velocity, uint3 cellPosition)
{
    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset;
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStartRO[cell]; j < BoundaryCellStartRO[cell + 1]; j++)
        {
            uint boundary = BoundarySortedIndicesRO[j];
            float4 boundaryPosition = BoundaryPositionsRO[boundary];
            float magSq = DistanceSq(position, boundaryPosition);
            float mag = sqrt(magSq);
            float q = mag / interactionRadius;

            float4 r = (boundaryPosition - position) / max(mag, 1e-9);
            float inwardVelocity = dot(velocity - BoundaryVelocitiesRO[boundary], r);

            // Branchless mask: 0 when the particles coincide, are out of range or are separating
            float w = step(1e-12, magSq) * step(q, 1.0) * step(0.0, inwardVelocity);
            float4 impulse = ViscosityImpulse(dt, boundaryFriction, 0.0, q, inwardVelocity, r);

            AtomicAddForceBuffer(index, -w * impulse);
        }
    }
}

// ---------- Boundary grid: the same counting sort as the fluid grid ----------

[numthreads(64,1,1)]
void ClearBoundaryGrid (uint3 id : SV_DispatchThreadID)
{
    if (id.x > numCells) return;

    BoundaryCellStart[id.x] = 0;
}

[numthreads(64,1,1)]
void CountBoundaryParticles (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    uint cellIndex = GetCellIndex(GetCellPosition(BoundaryPositions[id.x]));
    InterlockedAdd(BoundaryCellStart[cellIndex + 1], 1);
}

[numthreads(64,1,1)]
void BoundaryScanStep (uint3 id : SV_DispatchThreadID)
{
    if (id.x > numCells) return;

    uint previous = max((int)id.x - (int)scanStride, 0);
    uint mask = id.x >= scanStride; // 0 for the first stride entries, which carry nothing

    // scanFlip is uniform across the dispatch, so this costs no divergence
    if (scanFlip == 0)
        ScanTemp[id.x] = BoundaryCellStart[id.x] + BoundaryCellStart[previous] * mask;
    else
        BoundaryCellStart[id.x] = ScanTemp[id.x] + ScanTemp[previous] * mask;
}

[numthreads(64,1,1)]
void ResetBoundaryCursor (uint3 id : SV_DispatchThreadID)
{
    if (id.x > numCells) return;

    CellCursor[id.x] = BoundaryCellStart[id.x];
}

[numthreads(64,1,1)]
void ScatterBoundary (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    uint cellIndex = GetCellIndex(GetCellPosition(BoundaryPositions[id.x]));
    uint destination;
    InterlockedAdd(CellCursor[cellIndex], 1, destination);

    BoundarySortedIndices[destination] = id.x;
}

// ---------- Boundary particles ----------

// One-time volumes, V = 1 / sum of W over the boundary neighbours (port of 2D). Neighbours from other bodies
// are masked out, so a volume doesn't depend on where the bodies start
[numthreads(64,1,1)]
void CalculateBoundaryVolumes (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    float4 position = BoundaryPositions[id.x];
    uint body = BoundaryBodyIndices[id.x];
    uint3 cellPosition = GetCellPosition(position);
    float delta = 0;

    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset; // negatives wrap and fail the bounds check
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStart[cell]; j < BoundaryCellStart[cell + 1]; j++)
        {
            uint neighbour = BoundarySortedIndices[j];
            float magSq = DistanceSq(position, BoundaryPositions[neighbour]);
            float q = sqrt(magSq) / interactionRadius;

            // Branchless mask: 0 when the particles coincide, are out of range or belong to different bodies
            float w = step(1e-12, magSq) * step(q, 1.0) * (BoundaryBodyIndices[neighbour] == body);
            delta += w * QuadraticSpikyKernel(q);
        }
    }

    BoundaryVolumes[id.x] = 1.0 / delta;
}

[numthreads(64,1,1)]
void ClearBoundaryForceBuffers (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    SetBoundaryForceBuffer(id.x, ZeroImpulse);
}

// Rebuilds each boundary particle from its body's state: the position from the rest offset, and the velocity
// of that point of the body, which the fluid friction reads
[numthreads(64,1,1)]
void PlaceBoundaryParticles (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    RigidBodyState state = BodyStates[BoundaryBodyIndices[id.x]];
    float3 worldOffset = QuaternionRotate(state.rotation, BoundaryRestPositions[id.x].xyz);

    BoundaryPositions[id.x] = float4(state.position.xyz + worldOffset, 0);
    BoundaryVelocities[id.x] = float4(state.velocity.xyz + cross(state.angularVelocity.xyz, worldOffset), 0);
}

// ---------- Body integration: one thread group per body ----------

// Gravity and predicted state, the body counterpart of ExternalForces and AdvancePredictedPositions
[numthreads(1,1,1)]
void BodyAdvancePredictedStates (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyState state = BodyStates[id.x];

    state.velocity.y += dt * gravity;
    BodyForces[id.x * BodyForceCount + ForceGravity] = float4(0, gravity / BodyProperties[id.x].invMass, 0, 0);
    state.prevPosition = state.position;
    state.prevRotation = state.rotation;
    state.position += dt * state.velocity;
    state.rotation = IntegrateRotation(state.rotation, dt * state.angularVelocity.xyz);

    BodyStates[id.x] = state;
}

// Strided reduction of the fluid's push on a body. Thread s of group b adds up every BodyReductionThreads-th
// boundary particle of body b, starting at its s-th one, and writes only its own slot
[numthreads(BodyReductionThreads,1,1)]
void SumCouplingForces (uint3 groupId : SV_GroupID, uint3 threadId : SV_GroupThreadID)
{
    uint body = groupId.x;
    if (body >= numBodies) return;

    RigidBodyProperties properties = BodyProperties[body];
    float3 centerOfMass = BodyStates[body].position.xyz;

    float3 force = 0;
    float3 torque = 0;
    for (uint i = properties.start + threadId.x; i < properties.start + properties.count; i += BodyReductionThreads)
    {
        float3 push = ReadBoundaryForceBuffer(i);
        force += push;
        torque += cross(BoundaryPositionsRO[i].xyz - centerOfMass, push);
    }

    uint slot = (body * BodyReductionThreads + threadId.x) * CouplingSlotSize;
    BodyCouplingSums[slot] = float4(force, 0);
    BodyCouplingSums[slot + 1] = float4(torque, 0);
}

// Moves each body by the fluid's push: position by F / M, rotation by the inverse inertia times the torque
[numthreads(1,1,1)]
void ApplyCouplingForces (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyProperties properties = BodyProperties[id.x];
    RigidBodyState state = BodyStates[id.x];

    float3 force = 0;
    float3 torque = 0;
    [loop] for (uint s = 0; s < BodyReductionThreads; s++)
    {
        uint slot = (id.x * BodyReductionThreads + s) * CouplingSlotSize;
        force += BodyCouplingSums[slot].xyz;
        torque += BodyCouplingSums[slot + 1].xyz;
    }

    // The push moves the position alone, so it all becomes velocity: dv = invMass * force / dt, and F = M dv / dt
    BodyForces[id.x * BodyForceCount + ForceBuoyancy] = float4(force / (dt * dt), 0);

    float3x3 invInertiaWorld = InverseInertiaWorld(state.rotation, properties.invInertia.xyz);
    state.position.xyz += properties.invMass * force;
    state.rotation = IntegrateRotation(state.rotation, ClampRotation(mul(invInertiaWorld, torque), MaxCorrectionAngle));

    BodyStates[id.x] = state;
}

// Right mouse pulls bodies, the body counterpart of AttractToMouse
[numthreads(1,1,1)]
void AttractBodiesToMouse (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyState state = BodyStates[id.x];
    float mag = Distance(state.position, mousePosition);
    float inRange = step(mag, mouseRadius);

    state.position += inRange * dt * mouseAttractiveness * (mousePosition - state.position) / max(mag, 1e-9);
    BodyStates[id.x] = state;
}

// ---------- Contacts with the walls and other bodies ----------

// How far a position has to move to get back inside the walls, zero on axes where it is inside (port of 2D)
float3 WallPushOut(float3 position)
{
    float3 outside = step(realHalfBoundSize.xyz, abs(position));
    return (sign(position) * realHalfBoundSize.xyz - position) * outside;
}

// One positional contact at offset r from the centre of mass. It adds the push out by depth along normal, the friction
// that undoes the contact point's tangential motion (capped at bodyFriction times the push), and the motion into the
// contact scaled by this body's share of it, which ApplyContacts takes out of the velocity
void AccumulateContact(inout float4 pushSum, inout float4 frictionSum, inout float4 torqueSum, inout float4 approachSum,
                       float invMass, float3x3 invInertiaWorld, float3 r, float3 normal, float depth,
                       float3 contactDisplacement, float share)
{
    float isContact = step(1e-9, depth);
    float normalLambda = depth / GeneralizedInverseMass(invMass, invInertiaWorld, r, normal);

    float3 tangentialMotion = contactDisplacement - dot(contactDisplacement, normal) * normal;
    float tangentialLength = length(tangentialMotion);
    float3 tangent = -tangentialMotion / max(tangentialLength, 1e-9);
    float tangentLambda = tangentialLength / GeneralizedInverseMass(invMass, invInertiaWorld, r, tangent);
    float frictionLambda = min(tangentLambda, bodyFriction * normalLambda);

    float3 push = normalLambda * normal * isContact;
    float3 friction = frictionLambda * tangent * isContact;

    pushSum += float4(push, isContact);
    frictionSum.xyz += friction;
    torqueSum.xyz += cross(r, push + friction);
    approachSum.xyz += normal * min(dot(contactDisplacement, normal), 0.0) * share * isContact;
}

// Strided reduction of every contact of a body's boundary particles, sorted into contact groups
[numthreads(BodyReductionThreads,1,1)]
void SumContacts (uint3 groupId : SV_GroupID, uint3 threadId : SV_GroupThreadID)
{
    uint body = groupId.x;
    if (body >= numBodies) return;

    RigidBodyProperties properties = BodyProperties[body];
    RigidBodyState state = BodyStates[body];
    float3x3 invInertiaWorld = InverseInertiaWorld(state.rotation, properties.invInertia.xyz);

    float4 pushes[ContactGroups];
    float4 frictions[ContactGroups];
    float4 torques[ContactGroups];
    float4 approaches[ContactGroups];
    for (uint g = 0; g < ContactGroups; g++)
    {
        pushes[g] = 0;
        frictions[g] = 0;
        torques[g] = 0;
        approaches[g] = 0;
    }

    for (uint i = properties.start + threadId.x; i < properties.start + properties.count; i += BodyReductionThreads)
    {
        float3 restOffset = BoundaryRestPositions[i].xyz;
        float3 r = QuaternionRotate(state.rotation, restOffset);
        float3 contactPosition = state.position.xyz + r;
        float3 displacement = contactPosition - (state.prevPosition.xyz + QuaternionRotate(state.prevRotation, restOffset));

        // The walls are axis-aligned, so each axis is its own constraint, and they never move, so this body takes
        // the whole correction
        float3 wallPush = WallPushOut(contactPosition);
        AccumulateContact(pushes[0], frictions[0], torques[0], approaches[0], properties.invMass, invInertiaWorld,
                          r, float3(sign(wallPush.x), 0, 0), abs(wallPush.x), displacement, 1.0);
        AccumulateContact(pushes[1], frictions[1], torques[1], approaches[1], properties.invMass, invInertiaWorld,
                          r, float3(0, sign(wallPush.y), 0), abs(wallPush.y), displacement, 1.0);
        AccumulateContact(pushes[2], frictions[2], torques[2], approaches[2], properties.invMass, invInertiaWorld,
                          r, float3(0, 0, sign(wallPush.z)), abs(wallPush.z), displacement, 1.0);

        // Other bodies: push out along their surface normals, keeping the share of the correction the masses give
        // this body. Friction and the approach use the motion relative to the other body's surface
        float3 bodyPush = 0;
        float3 otherDisplacement = 0;
        float touching = 0;
        float shareSum = 0;
        for (uint other = 0; other < numBodies; other++)
        {
            RigidBodyProperties otherProperties = BodyProperties[other];
            RigidBodyState otherState = BodyStates[other];

            float3 otherNormal;
            float otherDistance = BodySignedDistance(otherProperties, otherState, contactPosition, otherNormal);

            // Only pushes that move this body away from the other body's centre count. In a deep overlap, particles past
            // the other body's centre are closest to its far side, and pushing them out there pulls the bodies together
            float facing = step(0.0, dot(otherNormal, state.position.xyz - otherState.position.xyz));
            float inside = step(otherDistance, particleRadius) * facing * (other != body);
            float share = properties.invMass / (properties.invMass + otherProperties.invMass);

            bodyPush += otherNormal * (particleRadius - otherDistance) * share * inside;
            otherDisplacement += BodyPointDisplacement(otherState, contactPosition) * inside;
            touching += inside;
            shareSum += share * inside;
        }

        float bodyDepth = length(bodyPush);
        float touchingCount = max(touching, 1.0);
        AccumulateContact(pushes[3], frictions[3], torques[3], approaches[3], properties.invMass, invInertiaWorld,
                          r, bodyPush / max(bodyDepth, 1e-9), bodyDepth, displacement - otherDisplacement / touchingCount,
                          shareSum / touchingCount);
    }

    uint slot = (body * BodyReductionThreads + threadId.x) * ContactSlotSize;
    for (uint c = 0; c < ContactGroups; c++)
    {
        BodyContactSums[slot + c * 4] = pushes[c];
        BodyContactSums[slot + c * 4 + 1] = frictions[c];
        BodyContactSums[slot + c * 4 + 2] = torques[c];
        BodyContactSums[slot + c * 4 + 3] = approaches[c];
    }
}

// Adds up a body's contact slots and applies each group's average (Jacobi averaging), so hundreds of floor contacts
// neither overshoot nor dilute a handful of wall contacts.
// Velocity comes from the change in position, so the push out moves the previous position as well: resolving a
// penetration must not become velocity, or a body starting inside a wall is fired out of it. The motion into the
// contacts is removed from the velocity instead, which makes contacts inelastic, and friction moves the position
// alone so that it slows the body down
[numthreads(1,1,1)]
void ApplyContacts (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyProperties properties = BodyProperties[id.x];
    RigidBodyState state = BodyStates[id.x];
    float3x3 invInertiaWorld = InverseInertiaWorld(state.rotation, properties.invInertia.xyz);

    float3 pushChange = 0;
    float3 frictionChange = 0;
    float3 approachChange = 0;
    float3 angleChange = 0;
    for (uint g = 0; g < ContactGroups; g++)
    {
        float4 push = 0;
        float3 friction = 0;
        float3 torque = 0;
        float3 approach = 0;
        [loop] for (uint s = 0; s < BodyReductionThreads; s++)
        {
            uint slot = (id.x * BodyReductionThreads + s) * ContactSlotSize + g * 4;
            push += BodyContactSums[slot];
            friction += BodyContactSums[slot + 1].xyz;
            torque += BodyContactSums[slot + 2].xyz;
            approach += BodyContactSums[slot + 3].xyz;
        }

        float contacts = max(push.w, 1.0);
        pushChange += properties.invMass * push.xyz / contacts;
        frictionChange += properties.invMass * friction / contacts;
        angleChange += mul(invInertiaWorld, torque) / contacts;
        approachChange += approach / contacts;
    }

    // The push moves both positions, so only the friction and the removed approach change the velocity
    BodyForces[id.x * BodyForceCount + ForceContact] = float4((frictionChange - approachChange) / (properties.invMass * dt * dt), 0);

    state.position.xyz += pushChange + frictionChange;
    state.prevPosition.xyz += pushChange + approachChange;
    state.rotation = IntegrateRotation(state.rotation, ClampRotation(angleChange, MaxCorrectionAngle));

    BodyStates[id.x] = state;
}

// Velocities from the change in state, the body counterpart of CalculateVelocity
[numthreads(1,1,1)]
void BodyCalculateVelocity (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyState state = BodyStates[id.x];
    state.velocity = (state.position - state.prevPosition) / dt;

    // Rotation over the step as a quaternion. q and -q are the same rotation, flipping to w >= 0 takes the short way
    float4 change = QuaternionMultiply(state.rotation, QuaternionConjugate(state.prevRotation));
    float shortWay = step(0.0, change.w) * 2.0 - 1.0;
    state.angularVelocity = float4(2.0 * shortWay * change.xyz / dt, 0);

    BodyStates[id.x] = state;
}

// ---------- Fluid against bodies ----------

// Hard collision of fluid particles with every body: a particle closer than particleRadius to a surface is pushed
// out along the surface normal. Kernel pressure is soft and sampled at points, so squeezed particles can slip
// between boundary particles (port of the 2D circle and square body collisions)
[numthreads(64,1,1)]
void ResolveBodyCollisions (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numParticles) return;

    float3 position = Positions[id.x].xyz;

    for (uint body = 0; body < numBodies; body++)
    {
        float3 normal;
        float surfaceDistance = BodySignedDistance(BodyProperties[body], BodyStates[body], position, normal);
        float inside = step(surfaceDistance, particleRadius);

        position += normal * (particleRadius + collisionDamp - surfaceDistance) * inside;
    }

    Positions[id.x] = float4(position, 0);
}
