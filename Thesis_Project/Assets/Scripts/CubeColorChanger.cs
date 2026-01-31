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

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.name.Contains("Human"))
        {
            meshRenderer.material.color = contactColor;
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        meshRenderer.material.color = originalColor;
    }
}