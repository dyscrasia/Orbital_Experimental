using Orbital.Physics;
using Orbital.Strategy;
using UnityEngine;

namespace Orbital.Presentation
{
    /// <summary>
    /// Renders a captured-into-orbit rocket as a small colored triangle.
    /// Driven kinematically from the stored orbit parameters; does not interact
    /// with the physics simulation.
    /// </summary>
    public class OrbitingRocketView : MonoBehaviour
    {
        private CelestialBody _capturedBody;
        private float _orbitAngle;
        private float _orbitRadius;
        private float _orbitAngularSpeed;
        private int _orbitDirection;
        private LineRenderer _shape;

        // Read-only accessors used by TurnManager.ApplyColonisationCompletion
        // to reconstruct PlanetOwnership when a colonisation finishes.
        public float OrbitRadius       => _orbitRadius;
        public float OrbitAngle        => _orbitAngle;
        public float OrbitAngularSpeed => _orbitAngularSpeed;
        public int   OrbitDirection    => _orbitDirection;

        public void Initialize(CelestialBody capturedBody, PlanetOwnership ownership, Color ownerColor)
        {
            _capturedBody       = capturedBody;
            _orbitAngle         = ownership.OrbitAngle;
            _orbitRadius        = ownership.OrbitRadius;
            _orbitAngularSpeed  = ownership.OrbitAngularSpeed;
            _orbitDirection     = ownership.OrbitDirection;

            _shape = CreateTriangle(ownerColor);
            UpdatePosition();
        }

        private void Update()
        {
            if (_capturedBody == null || _shape == null) return;

            _orbitAngle += _orbitAngularSpeed * _orbitDirection * Time.deltaTime;
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            Vector2 radial = new Vector2(Mathf.Cos(_orbitAngle), Mathf.Sin(_orbitAngle));
            Vector2 pos = _capturedBody.Position + radial * _orbitRadius;
            transform.position = new Vector3(pos.x, pos.y, -0.2f);

            // Point the triangle in the direction of travel
            Vector2 tangent = new Vector2(-radial.y, radial.x) * _orbitDirection;
            float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
        }

        private LineRenderer CreateTriangle(Color color)
        {
            GameObject go = new GameObject("RocketShape");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 3;
            lr.startWidth = 0.07f;
            lr.endWidth = 0.07f;
            lr.numCapVertices = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = color;
            lr.endColor   = color;

            // Small triangle pointing "up" in local space (tip = +Y)
            const float h = 0.22f;
            const float w = 0.13f;
            lr.SetPosition(0, new Vector3(0f,  h * 0.6f, 0f));   // tip
            lr.SetPosition(1, new Vector3(-w, -h * 0.4f, 0f));   // bottom-left
            lr.SetPosition(2, new Vector3( w, -h * 0.4f, 0f));   // bottom-right

            return lr;
        }
    }
}
