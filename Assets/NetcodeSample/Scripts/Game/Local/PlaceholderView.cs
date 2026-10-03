using System.Collections.Generic;
using DPF.Unity;
using NetcodeSample.Simulation;
using UnityEngine;

namespace NetcodeSample.Game.Local
{
    /// <summary>
    /// Minimal visuals for the simulation until the real presentation layer exists: primitive cubes and bases,
    /// interpolated between the last two simulated ticks.
    /// </summary>
    public sealed class PlaceholderView
    {
        private static readonly Color s_redColor = new(0.9f, 0.2f, 0.2f);
        private static readonly Color s_blueColor = new(0.2f, 0.4f, 0.95f);

        private readonly Dictionary<long, UnitView> _views = new();
        private readonly Stack<Transform> _pool = new();
        private readonly List<long> _stale = new();
        private readonly Transform _root;
        private readonly Material[] _materials;

        public PlaceholderView(GameSimulation simulation)
        {
            _root = new GameObject("Placeholder View").transform;

            // Created at runtime for the placeholder only; a player build would need the shader referenced.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            _materials = new[] { CreateMaterial(shader, s_redColor), CreateMaterial(shader, s_blueColor) };

            float baseDiameter = simulation.Rules.BaseRadius.ToFloat() * 2f;
            foreach (Team team in new[] { Team.Red, Team.Blue })
            {
                GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseObject.name = $"{team} Base";
                baseObject.transform.SetParent(_root, false);
                baseObject.transform.position = simulation.Level.GetBasePosition(team).ToVector3() + (Vector3.up * 0.1f);
                baseObject.transform.localScale = new Vector3(baseDiameter, 0.1f, baseDiameter);
                baseObject.GetComponent<Renderer>().sharedMaterial = _materials[(int)team];
                Object.Destroy(baseObject.GetComponent<Collider>());
            }
        }

        /// <summary>Records the state after a step; call once per simulated tick.</summary>
        public void Capture(GameSimulation simulation)
        {
            foreach (long id in _views.Keys)
            {
                _stale.Add(id);
            }

            for (int slot = 0; slot < simulation.UnitCapacity; slot++)
            {
                ref readonly Unit unit = ref simulation.GetUnit(slot);
                if (!unit.IsAlive)
                {
                    continue;
                }

                Vector3 position = unit.Position.ToVector3();
                if (_views.TryGetValue(unit.Id, out UnitView view))
                {
                    view.Previous = view.Current;
                    view.Current = position;
                    _views[unit.Id] = view;
                    _stale.Remove(unit.Id);
                    continue;
                }

                float size = simulation.Rules.GetStats(unit.Kind).Radius.ToFloat() * 2f;
                Transform transform = Rent(unit.Team);
                transform.localScale = Vector3.one * size;
                _views.Add(unit.Id, new UnitView
                {
                    Transform = transform,
                    Previous = position,
                    Current = position,
                    HalfHeight = size * 0.5f,
                });
            }

            foreach (long id in _stale)
            {
                Return(_views[id].Transform);
                _views.Remove(id);
            }

            _stale.Clear();
        }

        /// <summary>Places every cube between its last two ticks; alpha is the fraction of the next tick elapsed.</summary>
        public void Render(float alpha)
        {
            foreach (UnitView view in _views.Values)
            {
                view.Transform.position = Vector3.Lerp(view.Previous, view.Current, alpha) + (Vector3.up * view.HalfHeight);
            }
        }

        public void Destroy()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }

            foreach (Material material in _materials)
            {
                Object.Destroy(material);
            }
        }

        private static Material CreateMaterial(Shader shader, Color color)
        {
            Material material = new(shader);
            material.SetColor("_BaseColor", color);
            return material;
        }

        private Transform Rent(Team team)
        {
            Transform transform;
            if (_pool.Count > 0)
            {
                transform = _pool.Pop();
                transform.gameObject.SetActive(true);
            }
            else
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(cube.GetComponent<Collider>());
                transform = cube.transform;
                transform.SetParent(_root, false);
            }

            transform.GetComponent<Renderer>().sharedMaterial = _materials[(int)team];
            return transform;
        }

        private void Return(Transform transform)
        {
            transform.gameObject.SetActive(false);
            _pool.Push(transform);
        }

        private struct UnitView
        {
            public Transform Transform;
            public Vector3 Previous;
            public Vector3 Current;
            public float HalfHeight;
        }
    }
}
