using Scripts.Runtime.Physics.Mono;
using Scripts.Runtime.Rendering.Mono;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Scripts.Runtime.UI.Mono
{
    public sealed class StatsOverlayBehaviour : MonoBehaviour
    {
        private const int MinBalls = 1;

        /// <summary>Only guards against an accidental ×2 into gigabytes: 36 B/ball → ~300 MB here.</summary>
        private const int MaxBalls = 8_388_608;

        [SerializeField]
        private BallPhysicsSimulationBehaviour simulation;

        [SerializeField]
        private BallRendererBehaviour ballRenderer;

        [SerializeField]
        [Min(0.05f)]
        [Tooltip("Seconds between readout refreshes. Frame and sim times are averaged over this window.")]
        private float refreshInterval = 0.25f;

        [SerializeField]
        [Range(0.5f, 4f)]
        [Tooltip("Scales the panel for high-DPI screens.")]
        private float uiScale = 1.5f;

        [SerializeField]
        [Range(200f, 800f)]
        [Tooltip("Panel width in unscaled pixels. Long lines wrap inside it and the panel grows downward.")]
        private float panelWidth = 360f;

        private float _windowSeconds;
        private int _windowFrames;
        private float _worstFrameSeconds;
        private double _windowSimMilliseconds;

        private string _ballsLine = "";
        private string _frameLine = "";
        private string _simLine = "";
        private string _stepsLine = "";
        private string _fieldLine = "";
        private string _trianglesLine = "";
        private string _shadowTrianglesLine = "";

        private void Update()
        {
            if (simulation == null)
            {
                return;
            }

            float frameSeconds = Time.unscaledDeltaTime;
            _windowSeconds += frameSeconds;
            _windowFrames++;
            _worstFrameSeconds = math.max(_worstFrameSeconds, frameSeconds);
            _windowSimMilliseconds += simulation.LastJobMilliseconds;

            if (!(_windowSeconds >= refreshInterval))
            {
                return;
            }
            
            RefreshTimingLines();
            RefreshCountLines();
            
            _windowSeconds = 0f;
            _windowFrames = 0;
            _worstFrameSeconds = 0f;
            _windowSimMilliseconds = 0.0;
        }

        private void RefreshTimingLines()
        {
            float meanFrameMs = _windowSeconds / _windowFrames * 1000f;
            float fps = _windowFrames / _windowSeconds;
            double meanSimMs = _windowSimMilliseconds / _windowFrames;

            _frameLine = $"Frame: {meanFrameMs:F2} ms  ({fps:F0} fps)   worst {_worstFrameSeconds * 1000f:F2} ms";
            _simLine = $"Sim (schedule to join): {meanSimMs:F2} ms";
        }

        private void RefreshCountLines()
        {
            _ballsLine = $"Balls: {simulation.Count:N0}";
            _stepsLine = $"Substeps this frame: {simulation.LastStepCount}  @ {simulation.FixedDeltaTime * 1000f:F2} ms   Radius: {simulation.Radius:F3} m";

            var grid = simulation.FieldGrid;
            _fieldLine = $"SDF: {grid.Resolution.x}x{grid.Resolution.y}x{grid.Resolution.z} @ {grid.CellSize:F3} m";

            if (ballRenderer == null)
            {
                return;
            }
            
            long mainTriangles = (long)simulation.Count * ballRenderer.TrianglesPerBall;
            int cascades = MainLightShadowCascades();
            _trianglesLine = $"Tris: {mainTriangles:N0} balls ({ballRenderer.TrianglesPerBall} per ball)";
            _shadowTrianglesLine = $"Shadow tris: {mainTriangles * cascades:N0} ({cascades} cascades)";
        }

        private static int MainLightShadowCascades()
        {
            var urpAsset = UniversalRenderPipeline.asset;
            if (urpAsset == null || !urpAsset.supportsMainLightShadows)
            {
                return 0;
            }
            return urpAsset.shadowCascadeCount;
        }

        private void OnGUI()
        {
            if (simulation == null)
            {
                return;
            }

            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            GUILayout.BeginArea(new Rect(10f, 10f, panelWidth, Screen.height / uiScale - 20f));
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(panelWidth));
            GUILayout.Label(_ballsLine);
            GUILayout.Label(_frameLine);
            GUILayout.Label(_simLine);
            GUILayout.Label(_stepsLine);
            GUILayout.Label(_fieldLine);
            GUILayout.Label(_trianglesLine);
            GUILayout.Label(_shadowTrianglesLine);

            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button("÷ 2"))
            {
                simulation.Resize(math.max(MinBalls, simulation.Count / 2));
                RefreshCountLines();
            }
            
            if (GUILayout.Button("× 2"))
            {
                simulation.Resize(math.min(MaxBalls, simulation.Count * 2));
                RefreshCountLines();
            }
            
            if (GUILayout.Button("Reset"))
            {
                simulation.Respawn();
            }
            
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
