/*
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org
*
* This software is provided 'as-is', without any express or implied
* warranty.  In no event will the authors be held liable for any damages
* arising from the use of this software.
* Permission is granted to anyone to use this software for any purpose,
* including commercial applications, and to alter it and redistribute it
* freely, subject to the following restrictions:
* 1. The origin of this software must not be misrepresented; you must not
* claim that you wrote the original software. If you use this software
* in a product, an acknowledgment in the product documentation would be
* appreciated but is not required.
* 2. Altered source versions must be plainly marked as such, and must not be
* misrepresented as being the original software.
* 3. This notice may not be removed or altered from any source distribution.
*/

// Point-to-point constraint
// Cdot = v2 - v1
//      = v2 + cross(w2, r2) - v1 - cross(w1, r1)
// J = [-I -r1_skew I r2_skew ]
// Identity used:
// w k % (rx i + ry j) = w * (-ry i + rx j)

// Angle constraint
// Cdot = w2 - w1
// J = [0 0 -1 0 0 1]
// K = invI1 + invI2
using System;
using Box2D.Common;

namespace Box2D.Dynamics.Joints
{
public class b2FrictionJoint : b2Joint
{
public b2FrictionJoint(b2FrictionJointDef def) : base(def)
{
    m_localAnchorA = def.localAnchorA;
    m_localAnchorB = def.localAnchorB;

    m_linearImpulse.SetZero();
    m_angularImpulse = 0.0f;

    m_maxForce = def.maxForce;
    m_maxTorque = def.maxTorque;
}

public virtual void InitVelocityConstraints(b2SolverData data)
{
    m_indexA = m_bodyA.m_islandIndex;
    m_indexB = m_bodyB.m_islandIndex;
    m_localCenterA = m_bodyA.m_sweep.localCenter;
    m_localCenterB = m_bodyB.m_sweep.localCenter;
    m_invMassA = m_bodyA.m_invMass;
    m_invMassB = m_bodyB.m_invMass;
    m_invIA = m_bodyA.m_invI;
    m_invIB = m_bodyB.m_invI;

    float aA = data.positions[m_indexA].a;
    b2Vec2 vA = data.velocities[m_indexA].v;
    float wA = data.velocities[m_indexA].w;

    float aB = data.positions[m_indexB].a;
    b2Vec2 vB = data.velocities[m_indexB].v;
    float wB = data.velocities[m_indexB].w;

    b2Rot qA = new b2Rot(aA);
    b2Rot qB = new b2Rot(aB);

    // Compute the effective mass matrix.
    m_rA = b2Math.b2Mul(qA, m_localAnchorA - m_localCenterA);
    m_rB = b2Math.b2Mul(qB, m_localAnchorB - m_localCenterB);

    // J = [-I -r1_skew I r2_skew]
    //     [ 0       -1 0       1]
    // r_skew = [-ry; rx]

    // Matlab
    // K = [ mA+r1y^2*iA+mB+r2y^2*iB,  -r1y*iA*r1x-r2y*iB*r2x,          -r1y*iA-r2y*iB]
    //     [  -r1y*iA*r1x-r2y*iB*r2x, mA+r1x^2*iA+mB+r2x^2*iB,           r1x*iA+r2x*iB]
    //     [          -r1y*iA-r2y*iB,           r1x*iA+r2x*iB,                   iA+iB]

    float mA = m_invMassA, mB = m_invMassB;
    float iA = m_invIA, iB = m_invIB;

    b2Mat22 K;
    K.ex.x = mA + mB + iA * m_rA.y * m_rA.y + iB * m_rB.y * m_rB.y;
    K.ex.y = -iA * m_rA.x * m_rA.y - iB * m_rB.x * m_rB.y;
    K.ey.x = K.ex.y;
    K.ey.y = mA + mB + iA * m_rA.x * m_rA.x + iB * m_rB.x * m_rB.x;

    m_linearMass = K.GetInverse();

    m_angularMass = iA + iB;
    if (m_angularMass > 0.0f)
    {
        m_angularMass = 1.0f / m_angularMass;
    }

    if (data.step.warmStarting)
    {
        // Scale impulses to support a variable time step.
        m_linearImpulse *= data.step.dtRatio;
        m_angularImpulse *= data.step.dtRatio;

        b2Vec2 P(m_linearImpulse.x, m_linearImpulse.y);
        vA -= mA * P;
        wA -= iA * (b2Cross(m_rA, P) + m_angularImpulse);
        vB += mB * P;
        wB += iB * (b2Cross(m_rB, P) + m_angularImpulse);
    }
    else
    {
        m_linearImpulse.SetZero();
        m_angularImpulse = 0.0f;
    }

    data.velocities[m_indexA].v = vA;
    data.velocities[m_indexA].w = wA;
    data.velocities[m_indexB].v = vB;
    data.velocities[m_indexB].w = wB;
}

public virtual void SolveVelocityConstraints(b2SolverData data)
{
    b2Vec2 vA = data.velocities[m_indexA].v;
    float wA = data.velocities[m_indexA].w;
    b2Vec2 vB = data.velocities[m_indexB].v;
    float wB = data.velocities[m_indexB].w;

    float mA = m_invMassA, mB = m_invMassB;
    float iA = m_invIA, iB = m_invIB;

    float h = data.step.dt;

    // Solve angular friction
    {
        float Cdot = wB - wA;
        float impulse = -m_angularMass * Cdot;

        float oldImpulse = m_angularImpulse;
        float maxImpulse = h * m_maxTorque;
        m_angularImpulse = b2Math.b2Clamp(m_angularImpulse + impulse, -maxImpulse, maxImpulse);
        impulse = m_angularImpulse - oldImpulse;

        wA -= iA * impulse;
        wB += iB * impulse;
    }

    // Solve linear friction
    {
        b2Vec2 Cdot = vB + b2Math.b2Cross(wB, m_rB) - vA - b2Cross(wA, m_rA);

        b2Vec2 impulse = -b2Mul(m_linearMass, Cdot);
        b2Vec2 oldImpulse = m_linearImpulse;
        m_linearImpulse += impulse;

        float maxImpulse = h * m_maxForce;

        if (m_linearImpulse.LengthSquared() > maxImpulse * maxImpulse)
        {
            m_linearImpulse.Normalize();
            m_linearImpulse *= maxImpulse;
        }

        impulse = m_linearImpulse - oldImpulse;

        vA -= mA * impulse;
        wA -= iA * b2Cross(m_rA, impulse);

        vB += mB * impulse;
        wB += iB * b2Cross(m_rB, impulse);
    }

    data.velocities[m_indexA].v = vA;
    data.velocities[m_indexA].w = wA;
    data.velocities[m_indexB].v = vB;
    data.velocities[m_indexB].w = wB;
}

public virtual bool SolvePositionConstraints(b2SolverData data)
{
    return true;
}

public virtual b2Vec2 GetAnchorA()
{
    return m_bodyA.GetWorldPoint(m_localAnchorA);
}

public virtual b2Vec2 GetAnchorB()
{
    return m_bodyB.GetWorldPoint(m_localAnchorB);
}

public virtual b2Vec2 GetReactionForce(float inv_dt)
{
    return inv_dt * m_linearImpulse;
}

public virtual float GetReactionTorque(float inv_dt)
{
    return inv_dt * m_angularImpulse;
}

public virtual void SetMaxForce(float force)
{
    b2Assert(b2IsValid(force) && force >= 0.0f);
    m_maxForce = force;
}

public virtual float GetMaxForce(
{
    return m_maxForce;
}

public virtual void SetMaxTorque(float torque)
{
    b2Assert(b2IsValid(torque) && torque >= 0.0f);
    m_maxTorque = torque;
}

public virtual float GetMaxTorque(
{
    return m_maxTorque;
}

public virtual void Dump()
{
    int indexA = m_bodyA.m_islandIndex;
    int indexB = m_bodyB.m_islandIndex;

    b2Settings.b2Log("  b2FrictionJointDef jd;\n");
    b2Settings.b2Log("  jd.bodyA = bodies[{0}];\n", indexA);
    b2Settings.b2Log("  jd.bodyB = bodies[{0}];\n", indexB);
    b2Settings.b2Log("  jd.collideConnected = bool({0});\n", m_collideConnected);
    b2Settings.b2Log("  jd.localAnchorA.Set({0:F5}, {1:F5});\n", m_localAnchorA.x, m_localAnchorA.y);
    b2Settings.b2Log("  jd.localAnchorB.Set({0:F5}, {1:F5});\n", m_localAnchorB.x, m_localAnchorB.y);
    b2Settings.b2Log("  jd.maxForce = {0:F5};\n", m_maxForce);
    b2Settings.b2Log("  jd.maxTorque = {0:F5};\n", m_maxTorque);
    b2Settings.b2Log("  joints[{0}] = m_world.CreateJoint(&jd);\n", m_index);
}
}
}
