using Scripts.Runtime.Physics.Mono;
using Scripts.Runtime.Rendering.Mono;
using Scripts.Runtime.Sdf.Data;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Scripts.Runtime.UI.Mono
{
    /// <summary>
    /// The on-screen readout the brief asks for, plus the three buttons that answer "where does it
    /// fall over". IMGUI: no assets, one script, and nothing here touches the simulation or the
    /// renderer beyond public getters and the two count operations.
    ///
    /// Numbers are averaged over a refresh window and the label strings are rebuilt only then, so
    /// the per-frame cost is IMGUI's own layout and nothing of ours.
    /// </summary>
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

        [SerializeField]
        [Tooltip("Disables VSync and the frame-rate cap so the readout measures the work, not the monitor.")]
        private bool uncapFrameRate = true;

        // Accumulated since the last refresh.
        private float windowSeconds;
        private int windowFrames;
        private float worstFrameSeconds;
        private double windowSimMilliseconds;

        // Rebuilt on refresh, drawn every frame.
        private string ballsLine = "";
        private string frameLine = "";
        private string simLine = "";
        private string stepsLine = "";
        private string fieldLine = "";
        private string trianglesLine = "";
        private string shadowTrianglesLine = "";

        private void Start()
        {
            if (uncapFrameRate)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
        }

        private void Update()
        {
            if (simulation == null)
            {
                return;
            }

            // Unscaled: the readout must not lie when someone changes Time.timeScale.
            float frameSeconds = Time.unscaledDeltaTime;
            windowSeconds += frameSeconds;
            windowFrames++;
            worstFrameSeconds = math.max(worstFrameSeconds, frameSeconds);
            windowSimMilliseconds += simulation.LastJobMilliseconds;

            if (windowSeconds >= refreshInterval)
            {
                RefreshTimingLines();
                RefreshCountLines();
                windowSeconds = 0f;
                windowFrames = 0;
                worstFrameSeconds = 0f;
                windowSimMilliseconds = 0.0;
            }
        }

        /// <summary>Lines that average over the window. Only valid once the window holds a frame.</summary>
        private void RefreshTimingLines()
        {
            float meanFrameMs = windowSeconds / windowFrames * 1000f;
            float fps = windowFrames / windowSeconds;
            double meanSimMs = windowSimMilliseconds / windowFrames;

            frameLine = $"Frame: {meanFrameMs:F2} ms  ({fps:F0} fps)   worst {worstFrameSeconds * 1000f:F2} ms";
            simLine = $"Sim (schedule to join): {meanSimMs:F2} ms";
        }

        /// <summary>Lines that depend on the count and settings. Safe at any time; the buttons call it.</summary>
        private void RefreshCountLines()
        {
            ballsLine = $"Balls: {simulation.Count:N0}";
            stepsLine = $"Substeps this frame: {simulation.LastStepCount}  @ {simulation.FixedDeltaTime * 1000f:F2} ms   Radius: {simulation.Radius:F3} m";

            SdfGridData grid = simulation.FieldGrid;
            fieldLine = $"SDF: {grid.Resolution.x}x{grid.Resolution.y}x{grid.Resolution.z} @ {grid.CellSize:F3} m";

            if (ballRenderer != null)
            {
                long mainTriangles = (long)simulation.Count * ballRenderer.TrianglesPerBall;
                int cascades = MainLightShadowCascades();
                trianglesLine = $"Tris: {mainTriangles:N0} balls ({ballRenderer.TrianglesPerBall} per ball)";
                shadowTrianglesLine = $"Shadow tris: {mainTriangles * cascades:N0} ({cascades} cascades)";
            }
        }

        /// <summary>
        /// The balls are drawn once more per shadow cascade. Read from the active URP asset so the
        /// number follows the project settings instead of being a constant that can go stale.
        /// </summary>
        private static int MainLightShadowCascades()
        {
            UniversalRenderPipelineAsset urp = UniversalRenderPipeline.asset;
            if (urp == null || !urp.supportsMainLightShadows)
            {
                return 0;
            }
            return urp.shadowCascadeCount;
        }

        private void OnGUI()
        {
            if (simulation == null)
            {
                return;
            }

            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            // Fixed width in the corner; the area is given the full screen height so the box inside
            // can grow downward as lines wrap, instead of clipping the buttons off a fixed height.
            GUILayout.BeginArea(new Rect(10f, 10f, panelWidth, Screen.height / uiScale - 20f));
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(panelWidth));
            GUILayout.Label(ballsLine);
            GUILayout.Label(frameLine);
            GUILayout.Label(simLine);
            GUILayout.Label(stepsLine);
            GUILayout.Label(fieldLine);
            GUILayout.Label(trianglesLine);
            GUILayout.Label(shadowTrianglesLine);

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
