using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class LevelEditor : EditorWindow
{
    [MenuItem("Tools/Level Editor")]
    public static void OpenWindow() => GetWindow<LevelEditor>("Level Editor");

    private GameObject[] allPrefabs;
    private GameObject spawnPrefab = null;
    public Material previewMaterial;
    public Material snapPreviewMaterial;

    Vector3 previewPosition;
    Quaternion previewRotation = Quaternion.identity;

    GameObject previewInstance;
    private readonly Stack<GameObject> placedPrefabs = new();
    private float groundPlaneY = 0f;

    private void OnEnable()
    {
        SceneView.duringSceneGui += DuringSceneGUI;
        LoadPrefabs();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGUI;
        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
        }
    }

    private void LoadPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:prefab", new[] { "Assets/Prefabs" });
        IEnumerable<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath);
        allPrefabs = paths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
    }

    private void OnGUI()
    {
        GUILayout.Label("Select a Prefab", EditorStyles.boldLabel);

        if (allPrefabs == null || allPrefabs.Length == 0)
        {
            GUILayout.Label("No prefabs found in Assets/Prefabs.");
            return;
        }

        foreach (var prefab in allPrefabs)
        {
            if (GUILayout.Button(prefab.name))
            {
                spawnPrefab = prefab;
                CreatePreviewInstance();
            }
        }

        if (spawnPrefab != null)
        {
            GUILayout.Label("Selected Prefab: " + spawnPrefab.name);
            if (GUILayout.Button("Rotate 90°"))
            {
                RotatePreview();
            }
        }

        GUILayout.Label("Ground Plane Y", EditorStyles.boldLabel);
        groundPlaneY = EditorGUILayout.FloatField("Y:", groundPlaneY);

        if (placedPrefabs.Count > 0)
        {
            if (GUILayout.Button("Undo"))
            {
                CustomUndo();
            }
        }
    }

    private void CreatePreviewInstance()
    {
        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
        }

        previewInstance = Instantiate(spawnPrefab);
        ApplyPreviewMaterial(previewInstance, previewMaterial);
    }

    private void ApplyPreviewMaterial(GameObject instance, Material material)
    {
        if (material == null) return;

        foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterial = material;
        }
    }

    private void RotatePreview()
    {
        previewRotation *= Quaternion.Euler(0, 90, 0);
        previewInstance.transform.rotation = previewRotation;
    }

    void DuringSceneGUI(SceneView sceneView)
    {
        if (spawnPrefab == null)
        {
            return;
        }

        Event e = Event.current;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, groundPlaneY, 0));
        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            previewPosition = new Vector3(hitPoint.x, groundPlaneY, hitPoint.z);
            UpdatePreviewInstance();
        }

        if (e.type == EventType.MouseDown && e.button == 0 && previewInstance != null)
        {
            PlacePrefab();
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.R)
        {
            RotatePreview();
            e.Use();
        }

        sceneView.Repaint();
    }

    private void UpdatePreviewInstance()
    {
        if (previewInstance == null)
        {
            CreatePreviewInstance();
        }

        previewInstance.transform.position = previewPosition;
        previewInstance.transform.rotation = previewRotation;

        UpdatePreviewMaterial();
    }

    private void UpdatePreviewMaterial()
    {
        DoorIdentifier newDoorIdentifier = previewInstance.GetComponent<DoorIdentifier>();
        if (newDoorIdentifier == null)
        {
            return;
        }

        bool snapFound = false;

        foreach (GameObject placedPrefab in placedPrefabs)
        {
            DoorIdentifier placedDoorIdentifier = placedPrefab.GetComponent<DoorIdentifier>();
            if (placedDoorIdentifier == null)
            {
                continue;
            }

            foreach (Transform newSnapPoint in newDoorIdentifier.SnapPoints)
            {
                foreach (Transform placedSnapPoint in placedDoorIdentifier.SnapPoints)
                {
                    if (AreSnapPointsColliding(newSnapPoint, placedSnapPoint))
                    {
                        snapFound = true;
                        break;
                    }
                }
                if (snapFound)
                {
                    break;
                }
            }
            if (snapFound)
            {
                break;
            }
        }

        ApplyPreviewMaterial(previewInstance, snapFound ? snapPreviewMaterial : previewMaterial);
    }

    private bool AreSnapPointsColliding(Transform newSnapPoint, Transform placedSnapPoint)
    {
        Collider newCollider = newSnapPoint.GetComponent<Collider>();
        Collider placedCollider = placedSnapPoint.GetComponent<Collider>();

        if (newCollider == null || placedCollider == null)
        {
            return false;
        }

        return newCollider.bounds.Intersects(placedCollider.bounds);
    }

    private void PlacePrefab()
    {
        if (previewInstance == null)
        {
            return;
        }

        GameObject placedPrefab = Instantiate(spawnPrefab, previewPosition, previewRotation);
        SnapToExistingPrefab(placedPrefab);
        placedPrefabs.Push(placedPrefab);
    }

    private void SnapToExistingPrefab(GameObject newPrefab)
    {
        DoorIdentifier newDoorIdentifier = newPrefab.GetComponent<DoorIdentifier>();
        if (newDoorIdentifier == null)
        {
            return;
        }

        foreach (GameObject placedPrefab in placedPrefabs)
        {
            DoorIdentifier placedDoorIdentifier = placedPrefab.GetComponent<DoorIdentifier>();
            if (placedDoorIdentifier == null)
            {
                continue;
            }

            foreach (Transform newSnapPoint in newDoorIdentifier.SnapPoints)
            {
                foreach (Transform placedSnapPoint in placedDoorIdentifier.SnapPoints)
                {
                    if (AreSnapPointsColliding(newSnapPoint, placedSnapPoint))
                    {
                        Vector3 offset = placedSnapPoint.position - newSnapPoint.position;
                        newPrefab.transform.position += offset;
                        return;
                    }
                }
            }
        }
    }

    private void CustomUndo()
    {
        if (placedPrefabs.Count > 0)
        {
            GameObject lastPlacedPrefab = placedPrefabs.Pop();
            DestroyImmediate(lastPlacedPrefab);
        }
    }
}
