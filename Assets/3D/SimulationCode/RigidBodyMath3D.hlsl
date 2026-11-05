// Body structs, quaternion helpers and shape distances. Must stay in sync with RigidBodies3D.cs

static const uint ShapeCube = 0;
static const uint ShapeSphere = 1;
static const uint ShapeScale = 2;

// A scale is a box base with a round pan on top
#define MaxScales 2
static const float ScalePanRadius = 20;
static const float ScalePanHalfHeight = 1.5;

#define BodyReductionThreads 64

// Order matches BodyForce in Visualiser.cs
#define BodyForceCount 3
static const uint ForceGravity = 0;
static const uint ForceContact = 1;
static const uint ForceBuoyancy = 2;

static const float MaxCorrectionAngle = 0.2f;

struct RigidBodyProperties
{
    float4 size;    // Cube: half extents. Sphere: x = radius. Scale: base half extents, w = number
    float4 centerOfMass;
    float4 invInertia;
    uint shape;
    uint start;     // First boundary particle
    uint count;     // Number of boundary particles
    float invMass;  // 0 for scales, which only the CPU moves
};

struct RigidBodyState
{
    float4 position;  // Centre of mass
    float4 rotation;  // Quaternion (x, y, z, w)
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

// Rotates q by a small rotation
float4 IntegrateRotation(float4 q, float3 rotationVector)
{
    return normalize(q + 0.5 * QuaternionMultiply(float4(rotationVector, 0), q));
}

float3 ClampRotation(float3 rotationVector, float maxAngle)
{
    return rotationVector * min(1.0, maxAngle / max(length(rotationVector), 1e-9));
}

// ---------- Mass properties ----------

// Turns torque into spin, for the body as currently rotated
float3x3 InverseInertiaWorld(float4 rotation, float3 invInertia)
{
    float3 axisX = QuaternionRotate(rotation, float3(1, 0, 0));
    float3 axisY = QuaternionRotate(rotation, float3(0, 1, 0));
    float3 axisZ = QuaternionRotate(rotation, float3(0, 0, 1));

    float3x3 rotationMatrix = float3x3(axisX.x, axisY.x, axisZ.x,
                                       axisX.y, axisY.y, axisZ.y,
                                       axisX.z, axisY.z, axisZ.z);
    float3x3 scaled = float3x3(rotationMatrix[0] * invInertia,
                               rotationMatrix[1] * invInertia,
                               rotationMatrix[2] * invInertia);

    return mul(scaled, transpose(rotationMatrix));
}

// How easily a body moves when pushed at a point
float GeneralizedInverseMass(float invMass, float3x3 invInertiaWorld, float3 r, float3 direction)
{
    float3 rn = cross(r, direction);
    return invMass + dot(rn, mul(invInertiaWorld, rn));
}

// How far a point on the body moved since the last step
float3 BodyPointDisplacement(RigidBodyState state, float3 worldPosition)
{
    float3 bodyOffset = QuaternionRotateInverse(state.rotation, worldPosition - state.position.xyz);
    float3 previousPosition = state.prevPosition.xyz + QuaternionRotate(state.prevRotation, bodyOffset);
    return worldPosition - previousPosition;
}

// ---------- Shapes ----------

// Like sign(), but never 0
float3 SignNotZero(float3 v)
{
    return step(0.0, v) * 2.0 - 1.0;
}

// Shape distances: negative inside, with the outward normal
float SphereSignedDistance(float3 localPosition, float radius, out float3 localNormal)
{
    float len = length(localPosition);
    localNormal = localPosition / max(len, 1e-9);
    return len - radius;
}

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

float CylinderSignedDistance(float3 localPosition, float radius, float halfHeight, out float3 localNormal)
{
    float radialLength = length(localPosition.xz);
    float2 d = float2(radialLength - radius, abs(localPosition.y) - halfHeight);
    float2 outside = max(d, 0.0);
    float outsideLength = length(outside);

    float3 radial = float3(localPosition.x, 0, localPosition.z) / max(radialLength, 1e-9);
    float3 vertical = float3(0, step(0.0, localPosition.y) * 2.0 - 1.0, 0);
    float3 insideNormal = lerp(vertical, radial, step(d.y, d.x));
    float3 outsideNormal = (outside.x * radial + outside.y * vertical) / max(outsideLength, 1e-9);

    localNormal = lerp(insideNormal, outsideNormal, step(1e-9, outsideLength));
    return outsideLength + min(max(d.x, d.y), 0.0);
}

// Base and pan together
float ScaleSignedDistance(float3 localPosition, float3 baseHalfExtents, out float3 localNormal)
{
    float3 baseNormal;
    float baseDistance = BoxSignedDistance(localPosition, baseHalfExtents, baseNormal);

    float3 panNormal;
    float3 panCentre = float3(0, baseHalfExtents.y + ScalePanHalfHeight, 0);
    float panDistance = CylinderSignedDistance(localPosition - panCentre, ScalePanRadius, ScalePanHalfHeight, panNormal);

    localNormal = lerp(baseNormal, panNormal, step(panDistance, baseDistance));
    return min(baseDistance, panDistance);
}

// Distance from a point to any body's surface, negative inside, with the outward normal
float BodySignedDistance(RigidBodyProperties properties, RigidBodyState state, float3 worldPosition, out float3 worldNormal)
{
    float3 local = QuaternionRotateInverse(state.rotation, worldPosition - state.position.xyz) + properties.centerOfMass.xyz;

    float3 cubeNormal;
    float cubeDistance = BoxSignedDistance(local, properties.size.xyz, cubeNormal);

    float3 sphereNormal;
    float sphereDistance = SphereSignedDistance(local, properties.size.x, sphereNormal);

    float3 scaleNormal;
    float scaleDistance = ScaleSignedDistance(local, properties.size.xyz, scaleNormal);

    float isCube = properties.shape == ShapeCube;
    float isSphere = properties.shape == ShapeSphere;
    float isScale = properties.shape == ShapeScale;

    worldNormal = QuaternionRotate(state.rotation, cubeNormal * isCube + sphereNormal * isSphere + scaleNormal * isScale);
    return cubeDistance * isCube + sphereDistance * isSphere + scaleDistance * isScale;
}
