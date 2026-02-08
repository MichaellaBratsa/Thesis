using UnityEngine;

public class CubeColorChanger : MonoBehaviour
{
    public Color contactColor = Color.red;
    private Color originalColor;
    private MeshRenderer meshRenderer;

    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        originalColor = meshRenderer.material.color;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Player") || other.gameObject.name.Contains("Human"))
        {
            meshRenderer.material.color = contactColor;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Player") || other.gameObject.name.Contains("Human"))
        {
            meshRenderer.material.color = originalColor;
        }
    }
}