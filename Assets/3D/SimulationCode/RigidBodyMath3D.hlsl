// Rigid body data and math for the 3D simulation: body structs, quaternion helpers and shape signed distances.
// Pure functions only. Buffers are declared in Simulation.compute, the only file ComputeHelper reflection reads

// Shape ids, matching BodyShape3D in RigidBodies3D.cs
static const uint ShapeCube = 0;
static const uint ShapeSphere = 1;

// Threads per body in the strided reduction kernels, matching RigidBodies3D.ReductionThreads
#define BodyReductionThreads 64

// Forces on a body in BodyForces, in this order, matching BodyForce in Visualiser.cs and RigidBodies3D.ForceCount
#define BodyForceCount 3
static const uint ForceGravity = 0;
static const uint ForceContact = 1;
static const uint ForceBuoyancy = 2;

// Largest rotation, in radians, that one coupling or contact correction may apply
static const float MaxCorrectionAngle = 0.2f;

// Static per-body data uploaded from the CPU, mirrored by RigidBodyProperties3D
struct RigidBodyProperties
{
    float4 size;          // Cube: xyz = half extents. Sphere: x = radius
    float4 centerOfMass;  // In shape-local space, zero for uniform cubes and spheres
    float4 invInertia;    // Body-frame principal inverse inertia
    uint shape;
    uint start;           // First boundary particle of the body
    uint count;           // Number of boundary particles
    float invMass;
};

// Dynamic per-body data owned by the GPU after setup, mirrored by RigidBodyState3D.
// Rotations are quaternions stored as (x, y, z, w), so unlike positions their w carries data
struct RigidBodyState
{
    float4 position;      // Centre of mass
    float4 rotation;
    float4 prevPosition;
    float4 prevRotation;
    float4 velocity;
    float4 angularVelocity;
};

// ---------- Quaternions ----------

float4 QuaternionMultiply(float4 a, float4 b)
{
    return float4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz), a.w * b.w - dot(a.xyz, b.xyz));
}

float4 QuaternionConjugate(float4 q)
{
    return float4(-q.xyz, q.w);
}

float3 QuaternionRotate(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

float3 QuaternionRotateInverse(float4 q, float3 v)
{
    return QuaternionRotate(QuaternionConjugate(q), v);
}

// Rotates q by a small rotation vector (axis * angle) to first order, then renormalises
float4 IntegrateRotation(float4 q, float3 rotationVector)
{
    return normalize(q + 0.5 * QuaternionMultiply(float4(rotationVector, 0), q));
}

// Shortens a rotation vector to at most maxAngle radians without changing its axis
float3 ClampRotation(float3 rotationVector, float maxAngle)
{
    return rotationVector * min(1.0, maxAngle / max(length(rotationVector), 1e-9));
}

// ---------- Mass properties ----------

// World-space inverse inertia tensor: R * diag(invInertia) * transpose(R)
float3x3 InverseInertiaWorld(float4 rotation, float3 invInertia)
{
    float3 axisX = QuaternionRotate(rotation, float3(1, 0, 0));
    float3 axisY = QuaternionRotate(rotation, float3(0, 1, 0));
    float3 axisZ = QuaternionRotate(rotation, float3(0, 0, 1));

    // The rotated axes are the columns of R; scaling each row component-wise scales the columns
    float3x3 rotationMatrix = float3x3(axisX.x, axisY.x, axisZ.x,
                                       axisX.y, axisY.y, axisZ.y,
                                       axisX.z, axisY.z, axisZ.z);
    float3x3 scaled = float3x3(rotationMatrix[0] * invInertia,
                               rotationMatrix[1] * invInertia,
                               rotationMatrix[2] * invInertia);

    return mul(scaled, transpose(rotationMatrix));
}

// Inverse mass that a correction along direction sees when applied at offset r from the centre of mass
float GeneralizedInverseMass(float invMass, float3x3 invInertiaWorld, float3 r, float3 direction)
{
    float3 rn = cross(r, direction);
    return invMass + dot(rn, mul(invInertiaWorld, rn));
}

// How far the point of a body that is now at worldPosition has moved since the previous step
float3 BodyPointDisplacement(RigidBodyState state, float3 worldPosition)
{
    float3 bodyOffset = QuaternionRotateInverse(state.rotation, worldPosition - state.position.xyz);
    float3 previousPosition = state.prevPosition.xyz + QuaternionRotate(state.prevRotation, bodyOffset);
    return worldPosition - previousPosition;
}

// ---------- Shapes ----------

// 1 for components >= 0, -1 otherwise. sign() returns 0 at 0, which would zero a normal
float3 SignNotZero(float3 v)
{
    return step(0.0, v) * 2.0 - 1.0;
}

float SphereSignedDistance(float3 localPosition, float radius, out float3 localNormal)
{
    float len = length(localPosition);
    localNormal = localPosition / max(len, 1e-9);
    return len - radius;
}

// Outside the box the normal points away from the closest surface point, inside it follows the axis of least penetration
float BoxSignedDistance(float3 localPosition, float3 halfExtents, out float3 localNormal)
{
    float3 q = abs(localPosition) - halfExtents;
    float3 outside = max(q, 0.0);
    float outsideLength = length(outside);

    float isX = step(q.y, q.x) * step(q.z, q.x);
    float isY = (1.0 - isX) * step(q.z, q.y);
    float3 insideNormal = float3(isX, isY, (1.0 - isX) * (1.0 - isY));
    float3 outsideNormal = outside / max(outsideLength, 1e-9);

    float isOutside = step(1e-9, outsideLength);
    localNormal = lerp(insideNormal, outsideNormal, isOutside) * SignNotZero(localPosition);
    return outsideLength + min(max(q.x, max(q.y, q.z)), 0.0);
}

// Signed distance from a world position to a body's surface, with the outward world normal.
// Every shape is evaluated and masked by the body's shape id, so there is no branching.
// Adding a shape: an id above, its signed distance function, and a mask term here
float BodySignedDistance(RigidBodyProperties properties, RigidBodyState state, float3 worldPosition, out float3 worldNormal)
{
    float3 local = QuaternionRotateInverse(state.rotation, worldPosition - state.position.xyz) + properties.centerOfMass.xyz;

    float3 cubeNormal;
    float cubeDistance = BoxSignedDistance(local, properties.size.xyz, cubeNormal);

    float3 sphereNormal;
    float sphereDistance = SphereSignedDistance(local, properties.size.x, sphereNormal);

    float isCube = properties.shape == ShapeCube;
    float isSphere = properties.shape == ShapeSphere;

    worldNormal = QuaternionRotate(state.rotation, cubeNormal * isCube + sphereNormal * isSphere);
    return cubeDistance * isCube + sphereDistance * isSphere;
}
