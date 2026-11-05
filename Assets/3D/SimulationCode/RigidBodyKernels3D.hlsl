// Rigid body kernels. Included at the end of Simulation.compute, which declares their buffers and pragmas

#define CouplingSlotSize 2  // force, torque

// Contacts are summed in groups: one per wall axis, one for other bodies, then one per scale
#define ScaleContactGroup 4
#define ContactGroups (ScaleContactGroup + MaxScales)
#define ContactSlotSize (ContactGroups * 4)

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

// ---------- Fluid-body coupling, called from CalculateLambdas, CalculateDeltaPositions and ApplyViscosity ----------

// Adds the density that nearby rigid body surfaces give a fluid particle
void AddBoundaryDensity(float4 position, uint3 cellPosition, inout float density, inout float4 gradient)
{
    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset;  // negatives wrap and fail the bounds check
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStartRO[cell]; j < BoundaryCellStartRO[cell + 1]; j++)
        {
            uint boundary = BoundarySortedIndicesRO[j];
            float4 boundaryPosition = BoundaryPositionsRO[boundary];
            float magSq = DistanceSq(position, boundaryPosition);
            float mag = sqrt(magSq);

            float w = step(1e-12, magSq) * step(magSq, interactionRadiusSq);
            float volume = BoundaryVolumesRO[boundary];
            float4 r = (boundaryPosition - position) / max(mag, 1e-9);

            density += w * volume * Poly6(magSq, interactionRadiusSq, poly6Factor);
            gradient += w * volume * SpikyGradient(mag, interactionRadius, spikyGradFactor, r);
        }
    }
}

// Pushes a fluid particle away from rigid bodies, and records the opposite push on the body
void AddBoundaryCorrection(float4 position, uint3 cellPosition, float lambda, inout float4 delta)
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

            float w = step(1e-12, magSq) * step(magSq, interactionRadiusSq);
            float4 r = (boundaryPosition - position) / max(mag, 1e-9);
            float4 correction = w * lambda * BoundaryVolumesRO[boundary] *
                SpikyGradient(mag, interactionRadius, spikyGradFactor, r);

            delta += correction;
            AtomicAddBoundaryForceBuffer(boundary, -correction);
        }
    }
}

// Slows fluid that moves into a rigid body
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

            float w = step(1e-12, magSq) * step(q, 1.0) * step(0.0, inwardVelocity);
            float4 impulse = ViscosityImpulse(dt, boundaryFriction, 0.0, q, inwardVelocity, r);

            AtomicAddForceBuffer(index, -w * impulse);
        }
    }
}

// ---------- Boundary grid: the same counting sort as the fluid grid ----------

// Sorts the boundary particles into grid cells, the same way as the fluid
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
    uint mask = id.x >= scanStride;

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

// How much fluid each boundary particle stands in for. Only changes when the bodies are rebuilt
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
        uint3 neighbourPos = cellPosition + cellOffset;
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStart[cell]; j < BoundaryCellStart[cell + 1]; j++)
        {
            uint neighbour = BoundarySortedIndices[j];
            float magSq = DistanceSq(position, BoundaryPositions[neighbour]);

            float w = BoundaryBodyIndices[neighbour] == body;
            delta += w * Poly6(magSq, interactionRadiusSq, poly6Factor);
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

// Moves each boundary particle to follow its body
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

// Scales are static: only the CPU moves them
bool IsStatic(uint body)
{
    return BodyProperties[body].invMass == 0;
}

// Applies gravity to each body and moves it to its predicted position
[numthreads(1,1,1)]
void BodyAdvancePredictedStates (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    BodyForces[id.x * BodyForceCount + ForceBuoyancy] = 0;

    if (IsStatic(id.x))
    {
        BodyForces[id.x * BodyForceCount + ForceGravity] = 0;
        return;
    }

    RigidBodyState state = BodyStates[id.x];

    state.velocity.y += dt * gravity;
    BodyForces[id.x * BodyForceCount + ForceGravity] = float4(0, gravity / BodyProperties[id.x].invMass, 0, 0);
    state.prevPosition = state.position;
    state.prevRotation = state.rotation;
    state.position += dt * state.velocity;
    state.rotation = IntegrateRotation(state.rotation, dt * state.angularVelocity.xyz);

    BodyStates[id.x] = state;
}

// Adds up the fluid's push on each body, split across the group's threads
[numthreads(BodyReductionThreads,1,1)]
void SumCouplingForces (uint3 groupId : SV_GroupID, uint3 threadId : SV_GroupThreadID)
{
    uint body = groupId.x;
    if (body >= numBodies || IsStatic(body)) return;

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

// Moves each body by the fluid's push and records the buoyancy force.
// ponytail: light bodies may jitter, because fluid and bodies are solved one after the other
[numthreads(1,1,1)]
void ApplyCouplingForces (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies || IsStatic(id.x)) return;

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

    force *= particleMass;
    torque *= particleMass;

    BodyForces[id.x * BodyForceCount + ForceBuoyancy] += float4(force / (dt * dt), 0);

    float3x3 invInertiaWorld = InverseInertiaWorld(state.rotation, properties.invInertia.xyz);
    state.position.xyz += properties.invMass * force;
    state.rotation = IntegrateRotation(state.rotation, ClampRotation(mul(invInertiaWorld, torque), MaxCorrectionAngle));

    BodyStates[id.x] = state;
}

// Right mouse pulls bodies towards the cursor
[numthreads(1,1,1)]
void AttractBodiesToMouse (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies || IsStatic(id.x)) return;

    RigidBodyState state = BodyStates[id.x];
    float mag = Distance(state.position, mousePosition);
    float inRange = step(mag, mouseRadius);

    state.position += inRange * dt * mouseAttractiveness * (mousePosition - state.position) / max(mag, 1e-9);
    BodyStates[id.x] = state;
}

// ---------- Contacts with the walls and other bodies ----------

// How far a point must move to get back inside the tank. Leaves a gap so fluid fits under bodies
float3 WallPushOut(float3 position)
{
    float3 halfSize = realHalfBoundSize.xyz - restSpacing;
    float3 outside = step(halfSize, abs(position));
    return (sign(position) * halfSize - position) * outside;
}

// Adds one contact's push, friction and torque to the running totals
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

// Finds every contact of each body with the walls, other bodies and scales
[numthreads(BodyReductionThreads,1,1)]
void SumContacts (uint3 groupId : SV_GroupID, uint3 threadId : SV_GroupThreadID)
{
    uint body = groupId.x;
    if (body >= numBodies || IsStatic(body)) return;

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

        // Contacts with the tank walls
        float3 wallPush = WallPushOut(contactPosition);
        AccumulateContact(pushes[0], frictions[0], torques[0], approaches[0], properties.invMass, invInertiaWorld,
                          r, float3(sign(wallPush.x), 0, 0), abs(wallPush.x), displacement, 1.0);
        AccumulateContact(pushes[1], frictions[1], torques[1], approaches[1], properties.invMass, invInertiaWorld,
                          r, float3(0, sign(wallPush.y), 0), abs(wallPush.y), displacement, 1.0);
        AccumulateContact(pushes[2], frictions[2], torques[2], approaches[2], properties.invMass, invInertiaWorld,
                          r, float3(0, 0, sign(wallPush.z)), abs(wallPush.z), displacement, 1.0);

        // Contacts with other bodies and scales
        float3 bodyPush = 0;
        float3 otherDisplacement = 0;
        float touching = 0;
        float shareSum = 0;
        float3 scalePushes[MaxScales];
        for (uint k = 0; k < MaxScales; k++)
            scalePushes[k] = 0;

        for (uint other = 0; other < numBodies; other++)
        {
            RigidBodyProperties otherProperties = BodyProperties[other];
            RigidBodyState otherState = BodyStates[other];

            float3 otherNormal;
            float otherDistance = BodySignedDistance(otherProperties, otherState, contactPosition, otherNormal);

            float isScale = otherProperties.invMass == 0;
            float contactDistance = particleRadius + isScale * restSpacing;

            // Ignore pushes that would pull the two bodies together
            float facing = step(0.0, dot(otherNormal, state.position.xyz - otherState.position.xyz));
            float inside = step(otherDistance, contactDistance) * facing * (other != body);
            float share = properties.invMass / (properties.invMass + otherProperties.invMass);
            float3 push = otherNormal * (contactDistance - otherDistance) * inside;

            uint otherScale = (uint)otherProperties.size.w;
            for (uint s = 0; s < MaxScales; s++)
                scalePushes[s] += push * isScale * (float)(otherScale == s);

            float moving = inside * (1.0 - isScale);
            bodyPush += push * share * (1.0 - isScale);
            otherDisplacement += BodyPointDisplacement(otherState, contactPosition) * moving;
            touching += moving;
            shareSum += share * moving;
        }

        float bodyDepth = length(bodyPush);
        float touchingCount = max(touching, 1.0);
        AccumulateContact(pushes[3], frictions[3], torques[3], approaches[3], properties.invMass, invInertiaWorld,
                          r, bodyPush / max(bodyDepth, 1e-9), bodyDepth, displacement - otherDisplacement / touchingCount,
                          shareSum / touchingCount);

        for (uint scale = 0; scale < MaxScales; scale++)
        {
            uint group = ScaleContactGroup + scale;
            float scaleDepth = length(scalePushes[scale]);
            AccumulateContact(pushes[group], frictions[group], torques[group], approaches[group], properties.invMass,
                              invInertiaWorld, r, scalePushes[scale] / max(scaleDepth, 1e-9), scaleDepth, displacement, 1.0);
        }
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

// Contact force on a body, for the arrows and readouts
float3 ContactForce(float3 frictionChange, float3 approachChange, float invMass)
{
    return (frictionChange - approachChange) / (invMass * dt * dt);
}

// Moves each body out of whatever it's touching and applies friction.
// Also records how hard each scale pushes on it, for the scale readings
[numthreads(1,1,1)]
void ApplyContacts (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    if (IsStatic(id.x))
    {
        BodyForces[id.x * BodyForceCount + ForceContact] = 0;
        for (uint scale = 0; scale < MaxScales; scale++)
            BodyScaleForces[id.x * MaxScales + scale] = 0;
        return;
    }

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
        float3 groupFriction = properties.invMass * friction / contacts;
        float3 groupApproach = approach / contacts;
        pushChange += properties.invMass * push.xyz / contacts;
        frictionChange += groupFriction;
        angleChange += mul(invInertiaWorld, torque) / contacts;
        approachChange += groupApproach;

        if (g >= ScaleContactGroup)
            BodyScaleForces[id.x * MaxScales + g - ScaleContactGroup] = float4(ContactForce(groupFriction, groupApproach, properties.invMass), 0);
    }

    BodyForces[id.x * BodyForceCount + ForceContact] = float4(ContactForce(frictionChange, approachChange, properties.invMass), 0);

    state.position.xyz += pushChange + frictionChange;
    state.prevPosition.xyz += pushChange + approachChange;
    state.rotation = IntegrateRotation(state.rotation, ClampRotation(angleChange, MaxCorrectionAngle));

    BodyStates[id.x] = state;
}

// Each body's velocity from how far it moved and turned this step
[numthreads(1,1,1)]
void BodyCalculateVelocity (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyState state = BodyStates[id.x];
    state.velocity = (state.position - state.prevPosition) / dt;

    // Take the shorter way round
    float4 change = QuaternionMultiply(state.rotation, QuaternionConjugate(state.prevRotation));
    float shortWay = step(0.0, change.w) * 2.0 - 1.0;
    state.angularVelocity = float4(2.0 * shortWay * change.xyz / dt, 0);

    BodyStates[id.x] = state;
}

// Smooths the forces shown on screen so they don't flicker
[numthreads(1,1,1)]
void SmoothBodyForces (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    for (uint f = 0; f < BodyForceCount; f++)
    {
        uint i = id.x * BodyForceCount + f;
        SmoothedBodyForces[i] = lerp(SmoothedBodyForces[i], BodyForces[i], forceSmoothing);
    }
}

// ---------- Fluid against bodies ----------

// Pushes fluid particles out of any rigid body they ended up inside
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
