using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class MobDeathAnimation : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private AdjustDepthToHeigth depthAdjuster;
    [SerializeField] private Sprite[] frames;
    [SerializeField, Min(0.01f)] private float framesPerSecond = 12f;
    [SerializeField, Min(0.01f)] private float fallbackLifetime = 1f;

    private float lifetime = 1f;
    private float elapsedTime;
    private bool initialized;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        if (!initialized)
        {
            return;
        }
        
        elapsedTime = 0f;
        ApplyFrame(0);
    }

    public void InitializeFrom(SpriteRenderer sourceRenderer)
    {
        ResolveReferences();

        if (sourceRenderer == null || spriteRenderer == null)
        {
            initialized = true;
            lifetime = ResolveLifetime();
            return;
        }

        transform.position = sourceRenderer.transform.position;
        transform.rotation = sourceRenderer.transform.rotation;

        spriteRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        spriteRenderer.color = sourceRenderer.color;
        spriteRenderer.flipX = sourceRenderer.flipX;
        spriteRenderer.flipY = sourceRenderer.flipY;
        spriteRenderer.maskInteraction = sourceRenderer.maskInteraction;
        spriteRenderer.sortingLayerID = sourceRenderer.sortingLayerID;

        if (depthAdjuster == null)
        {
            spriteRenderer.sortingOrder = sourceRenderer.sortingOrder;
        }

        if (spriteRenderer.sprite != null && sourceRenderer.bounds.size.y > 0.0001f)
        {
            float spriteHeight = spriteRenderer.sprite.bounds.size.y;
            if (spriteHeight > 0.0001f)
            {
                float targetScale = sourceRenderer.bounds.size.y / spriteHeight;
                transform.localScale = new Vector3(1.2f, 1.2f, 1) * targetScale;
            }
        }

        initialized = true;
        lifetime = ResolveLifetime();
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        elapsedTime += Time.deltaTime;
        ApplyFrame(Mathf.FloorToInt(elapsedTime * framesPerSecond));

        if (elapsedTime >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    private float ResolveLifetime()
    {
        if (frames == null || frames.Length == 0 || framesPerSecond <= 0f)
        {
            return fallbackLifetime;
        }

        return frames.Length / framesPerSecond;
    }

    private void ResolveReferences()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (depthAdjuster == null)
        {
            depthAdjuster = GetComponent<AdjustDepthToHeigth>();
        }
    }

    private void ApplyFrame(int frameIndex)
    {
        if (spriteRenderer == null || frames == null || frames.Length == 0)
        {
            return;
        }

        int clampedIndex = Mathf.Clamp(frameIndex, 0, frames.Length - 1);
        if (frames[clampedIndex] != null)
        {
            spriteRenderer.sprite = frames[clampedIndex];
        }
    }
}
