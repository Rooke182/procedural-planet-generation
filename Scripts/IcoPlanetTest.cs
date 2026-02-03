using System;
using System.Linq;
using UnityEngine;

public class IcoPlanetTest : MonoBehaviour
{
    // Generates an icosphere mesh and displaces vertices using a compute shader
    // to create procedural planet terrain. Designed to keep generation fast at
    // high resolutions (UInt32 indices) and to keep the pipeline data-oriented:
    // mesh input -> GPU displacement -> CPU colour mapping -> Unity Mesh output.

    [SerializeField] ComputeShader m_computeShader;
    [SerializeField] Mesh m_mesh;
    [SerializeField] MeshFilter m_meshFilter;
    [SerializeField] SimplexNoiseAttributes m_simplexNoiseAttributes;
    [SerializeField] Gradient m_gradient;
    [SerializeField] AsteroidGenerator m_asteroidGenerator;
    [SerializeField] Iterations m_iterations;
    [SerializeField] GameObject m_water;
    [Range(0, 7)]
    [SerializeField] int m_resolution;
    [SerializeField] bool m_randomiseValues;
    [SerializeField] bool m_refresh;
    [SerializeField] bool m_seedRandomize;
    [SerializeField] bool m_generateMeshCollider;

    private Color[] m_Colours_Float4;
    private Vector3[] m_MaxVertices;
    private Vector3[] m_Vertices;
    private Triangle[] m_Polygons;
    private ComputeBuffer vertexBuffer;
    private float[] m_Colours;
    private int m_vertexCount;
    private bool m_hasGenerated;

    private const int MaxAsteroidChance = 6;
    private const int MaxAsteroids = 200;
    private const float MinAsteroidOffset = 200.0f;
    private const float MaxAsteroidOffset = 300.0f;
    private const int SeedRange = 10000;

    private void OnValidate()
    {
        if (m_refresh)
        {
            m_refresh = false;
            GenerateMesh();
        }
    }

    private void Start()
    {
        Initialize();
        GenerateMesh();

        if (m_asteroidGenerator && UnityEngine.Random.Range(0, MaxAsteroidChance) == 0)
        {
            GenerateAsteroids();
        }
    }

    private void Initialize()
    {
        m_hasGenerated = false;
        m_randomiseValues = true;
        m_generateMeshCollider = true;
    }

    private void GenerateAsteroids()
    {
        m_asteroidGenerator.numAsteroids = UnityEngine.Random.Range(0, MaxAsteroids);
        float offset = UnityEngine.Random.Range(MinAsteroidOffset, MaxAsteroidOffset) / 100.0f;
        m_asteroidGenerator.ringRadius = m_simplexNoiseAttributes.planetSize * offset;
        if (m_asteroidGenerator.numAsteroids != 0)
        {
            m_asteroidGenerator.SpawnAsteroids();
        }
    }

    public void GenerateMesh()
    {
        if (GetComponent<ProceduralIcoSphere>() && m_randomiseValues)
        {
            GetComponent<ProceduralIcoSphere>().RandomiseValues(this);
            m_randomiseValues = false;
        }

        m_simplexNoiseAttributes.mass = UnityEngine.Random.Range(m_simplexNoiseAttributes.planetSize * 5, m_simplexNoiseAttributes.planetSize * 15);

        if (GetComponent<GravitySet>())
        {
            GetComponent<GravitySet>().mass = m_simplexNoiseAttributes.mass;
            GetComponent<GravitySet>().UpdateCall();
        }

        transform.localScale = new Vector3(m_simplexNoiseAttributes.planetSize, m_simplexNoiseAttributes.planetSize, m_simplexNoiseAttributes.planetSize);

        if (m_seedRandomize)
        {
            m_seedRandomize = false;

            m_simplexNoiseAttributes.seed = UnityEngine.Random.Range(0, SeedRange);
        }

        GetMesh();
        Offset();

        CreateMesh();
    }

    private void GetMesh()
    {
        if (m_iterations == null)
        {
            m_iterations = FindObjectOfType<Iterations>();
        }


        if (m_iterations != null)
        {
            m_vertexCount = m_iterations.meshes[m_resolution].m_Vertices.Count();

            m_Vertices = new Vector3[m_vertexCount];

            m_Polygons = m_iterations.meshes[m_resolution].m_Polygons.ToArray();
            m_Vertices = m_iterations.meshes[m_resolution].m_Vertices.ToArray();
        }



        m_Colours = new float[m_vertexCount];
        m_Colours_Float4 = new Color[m_vertexCount];
        m_mesh = new Mesh();
    }

    private void Offset()
    {
        // Create arrays directly for vertices, UVs, and colors
        Vertex[] verticesArray = new Vertex[m_vertexCount];

        for (int i = 0; i < m_vertexCount; i++)
        {
            // Make sure m_UVs and m_Colours have enough elements
            float color = i < m_Colours.Count() ? m_Colours[i] : 0f;

            verticesArray[i] = new Vertex
            {
                position = m_Vertices[i],
                color = color
            };
        }

        vertexBuffer = new ComputeBuffer(m_vertexCount, sizeof(float) * 4); // Assuming 3 for position, 1 for color, adjust as needed
        vertexBuffer.SetData(verticesArray);

        // Set compute shader parameters for both vertices and UVs
        m_computeShader.SetBuffer(0, "vertices", vertexBuffer);

        // Set shader parameters
        m_computeShader.SetFloat("size", m_simplexNoiseAttributes.size);
        m_computeShader.SetFloat("cutOff", m_simplexNoiseAttributes.cutOff);
        m_computeShader.SetFloat("seed", m_simplexNoiseAttributes.seed);
        m_computeShader.SetFloat("persistence", m_simplexNoiseAttributes.persistence);
        m_computeShader.SetFloat("lacunarity", m_simplexNoiseAttributes.lacunarity);

        m_computeShader.SetInt("numOctaves", m_simplexNoiseAttributes.numOctaves);

        // Dispatch the compute shader
        m_computeShader.Dispatch(0, Mathf.CeilToInt(m_vertexCount / 512.0f), 1, 1);

        // Retrieve the modified data back for both vertices and UVs
        Vertex[] modifiedVertices = new Vertex[m_vertexCount];

        vertexBuffer.GetData(modifiedVertices);
        vertexBuffer.Release();

        m_Vertices = modifiedVertices.Select(v => v.position).ToArray();
        m_Colours = modifiedVertices.Select(v => v.color).ToArray();

        m_Colours_Float4 = m_Colours.Select(c => m_gradient.Evaluate(c)).ToArray();
    }

    void CreateMesh()
    {
        //mesh index format is 32 as this allows for resolutions higher than 6 (65,536)
        //7 (131,000 roughly)
        m_mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m_mesh.Clear();

        m_mesh.vertices = m_Vertices;

        int polygonCount = m_Polygons.Count();
        int trianglesCount = polygonCount * 3;

        // Preallocate arrays for triangles, UVs, and colors
        int[] triangles = new int[trianglesCount];

        int triangleIndex = 0;

        for (int i = 0; i < polygonCount; ++i)
        {
            triangles[triangleIndex++] = m_Polygons[i].vertices[0];
            triangles[triangleIndex++] = m_Polygons[i].vertices[1];
            triangles[triangleIndex++] = m_Polygons[i].vertices[2];
        }

        m_mesh.triangles = triangles;
        m_mesh.RecalculateNormals();
        m_mesh.colors = m_Colours_Float4;

        m_MaxVertices = new Vector3[m_Vertices.Length];
        m_MaxVertices = m_Vertices;

        if (m_generateMeshCollider && m_resolution == GetComponent<LOD>().distances.Length - 1)
        {
            m_generateMeshCollider = false;
            InitialiseMeshCollider();
            SpawnObjects();
        }

        m_meshFilter.mesh = m_mesh;
        m_hasGenerated = true;
    }

    private void SpawnObjects()
    {
        if (GetComponent<ObjectSpawner>() != null)
        {
            GetComponent<ObjectSpawner>().Spawn();
        }
    }

    private void InitialiseMeshCollider()
    {
        if (TryGetComponent(out MeshCollider meshCollider))
        {
            meshCollider.sharedMesh = m_mesh;
            meshCollider.sharedMesh.RecalculateBounds();
        }
    }

    //Get Set Variables
    [HideInInspector]
    public Vertex[] VertexList { get; private set; }

    public Vector3[] Vertices => m_Vertices;

    public Vector3[] MaxVertices => m_MaxVertices;

    public float[] Colors => m_Colours;

    public bool HasGenerated => m_hasGenerated;

    public float Mass => m_simplexNoiseAttributes.mass;

    public float PlanetSize
    {
        get => m_simplexNoiseAttributes.planetSize;
        set => m_simplexNoiseAttributes.planetSize = value;
    }

    public int Resolution
    {
        get => m_resolution;
        set => m_resolution = value;
    }

    public GameObject Water
    {
        set => m_water = value;
    }

    /// <summary>
    /// Sets Parameters for the compute shader
    /// </summary>
    public SimplexNoiseAttributes SimplexNoise
    {
        set
        {
            m_simplexNoiseAttributes.cutOff = value.cutOff;
            m_simplexNoiseAttributes.size = value.size;
            m_simplexNoiseAttributes.seed = value.seed;
            m_simplexNoiseAttributes.planetSize = value.planetSize;
            m_simplexNoiseAttributes.mass = value.mass;
            m_simplexNoiseAttributes.persistence = value.persistence;
            m_simplexNoiseAttributes.lacunarity = value.lacunarity;
            m_simplexNoiseAttributes.numOctaves = value.numOctaves;
        }
    }
}

[System.Serializable]
public class SimplexNoiseAttributes
{
    [Range(-1.1f, 1.1f)]
    public float cutOff;
    public float size;
    public int seed;
    public float planetSize = 10;
    public float mass;
    public float persistence;
    public float lacunarity;
    public int numOctaves;
}