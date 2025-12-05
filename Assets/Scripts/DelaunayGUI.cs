using UnityEngine;

/// <summary>
/// Simple IMGUI overlay for controlling Delaunay triangulation demo.
/// Attach this to the same GameObject as Particles2D and Triangles components.
/// </summary>
public class DelaunayGUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Particles2D particles;
    [SerializeField] private Triangles triangles;

    [Header("GUI Settings")]
    [SerializeField] private bool showGUI = true;
    [SerializeField] private Rect windowRect = new Rect(20, 20, 280, 400);
    [SerializeField] private KeyCode toggleKey = KeyCode.G;

    // Cached particle parameters (to avoid directly modifying serialized fields)
    private int targetParticleCount;
    private float repulsionStrength;
    private float gravity;
    private float dragPerSecond;
    private float maxVelocity;
    private float noiseScale;

    // GUI state
    private bool triangulationEnabled = false;
    private Vector2 scrollPosition;

    void Start()
    {
        // Auto-find components if not assigned
        if (particles == null)
            particles = GetComponent<Particles2D>();
        if (triangles == null)
            triangles = GetComponent<Triangles>();

        if (particles == null)
        {
            Debug.LogWarning("DelaunayGUI: No Particles2D component found!");
            enabled = false;
            return;
        }

        if (triangles == null)
        {
            Debug.LogWarning("DelaunayGUI: No Triangles component found!");
        }

        // Initialize cached values from Particles2D using reflection
        CacheParticleParameters();
    }

    void Update()
    {
        // Control triangulation component enabled state
        if (triangles != null)
        {
            triangles.enabled = triangulationEnabled;
        }
    }

    void OnGUI()
    {
        if (!showGUI) return;

        // Draw the control window
        windowRect = GUILayout.Window(0, windowRect, DrawControlWindow, "Controls");
    }

    void DrawControlWindow(int windowID)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Space(10);

        // Triangulation checkbox
        triangulationEnabled = GUILayout.Toggle(triangulationEnabled, " Triangulate");

        GUILayout.Space(15);

        // Particle Count
        GUILayout.Label($"# of Particles: {targetParticleCount}");
        targetParticleCount = (int)GUILayout.HorizontalSlider(targetParticleCount, 1, 200);

        GUILayout.Space(10);

        // Repulsion Strength
        GUILayout.Label($"Repulsion: {repulsionStrength:F2}");
        repulsionStrength = GUILayout.HorizontalSlider(repulsionStrength, 0f, 20f);

        GUILayout.Space(5);

        // Gravity
        GUILayout.Label($"Gravity: {gravity:F1}");
        gravity = GUILayout.HorizontalSlider(gravity, 0f, 100f);

        GUILayout.Space(5);

        // Drag
        GUILayout.Label($"Drag: {dragPerSecond:F2}");
        dragPerSecond = GUILayout.HorizontalSlider(dragPerSecond, 0f, 10f);

        GUILayout.Space(5);

        // Max Velocity
        GUILayout.Label($"Max Velocity: {maxVelocity:F1}");
        maxVelocity = GUILayout.HorizontalSlider(maxVelocity, 1f, 50f);

        GUILayout.Space(5);

        // Noise Scale
        GUILayout.Label($"Noise Scale: {noiseScale:F2}");
        noiseScale = GUILayout.HorizontalSlider(noiseScale, 0.1f, 100f);

        GUILayout.Space(15);

        // Apply changes button
        if (GUILayout.Button("Apply Changes & Reset", GUILayout.Height(30)))
        {
            triangulationEnabled = false;
            ApplyAllParameters();
        }

        GUILayout.EndScrollView();

        // Make window draggable
        GUI.DragWindow();
    }

    /// <summary>
    /// Cache current parameters from Particles2D using reflection.
    /// This allows us to read private/protected serialized fields.
    /// </summary>
    void CacheParticleParameters()
    {
        if (particles == null) return;

        var type = particles.GetType();
        var flags = System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.Public;

        targetParticleCount = GetFieldValue<int>(type, flags, "particleCount", 10);
        repulsionStrength = GetFieldValue<float>(type, flags, "repulsionStrength", 5f);
        gravity = GetFieldValue<float>(type, flags, "gravity", 30f);
        dragPerSecond = GetFieldValue<float>(type, flags, "dragPerSecond", 2.5f);
        maxVelocity = GetFieldValue<float>(type, flags, "maxVelocity", 10f);
        noiseScale = GetFieldValue<float>(type, flags, "noiseScale", 20f);
    }

    /// <summary>
    /// Helper to get field value via reflection with fallback default.
    /// </summary>
    T GetFieldValue<T>(System.Type type, System.Reflection.BindingFlags flags, string fieldName, T defaultValue)
    {
        try
        {
            var field = type.GetField(fieldName, flags);
            if (field != null)
                return (T)field.GetValue(particles);
        }
        catch { }
        return defaultValue;
    }

    /// <summary>
    /// Helper to set field value via reflection.
    /// </summary>
    void SetFieldValue<T>(System.Type type, System.Reflection.BindingFlags flags, string fieldName, T value)
    {
        try
        {
            var field = type.GetField(fieldName, flags);
            if (field != null)
                field.SetValue(particles, value);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to set {fieldName}: {e.Message}");
        }
    }

    /// <summary>
    /// Apply all cached parameters to Particles2D component.
    /// </summary>
    void ApplyAllParameters()
    {
        if (particles == null) return;

        var type = particles.GetType();
        var flags = System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.Public;

        SetFieldValue(type, flags, "particleCount", targetParticleCount);
        SetFieldValue(type, flags, "repulsionStrength", repulsionStrength);
        SetFieldValue(type, flags, "gravity", gravity);
        SetFieldValue(type, flags, "dragPerSecond", dragPerSecond);
        SetFieldValue(type, flags, "maxVelocity", maxVelocity);
        SetFieldValue(type, flags, "noiseScale", noiseScale);

        // Trigger validation to apply changes
        particles.SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);

        Debug.Log("Delaunay parameters applied!");
    }
}
