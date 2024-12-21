using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapPainter : MonoBehaviour
{
    [SerializeField] private Mapper mapper;

    List<List<short>> map;

    public RenderTexture renderTexture; // renderTextuer that you will be rendering stuff on
    public Renderer renderer; // renderer in which you will apply changed texture
    Texture2D texture;


    // Start is called before the first frame update
    void Start()
    {
        texture = new Texture2D(renderTexture.width, renderTexture.height);
        renderer.material.mainTexture = texture;
    }

    // Update is called once per frame
    void Update()
    {
        map = mapper.map2D;

        RenderTexture.active = renderTexture;
        //don't forget that you need to specify rendertexture before you call readpixels
        //otherwise it will read screen pixels.
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);

        for (int i = 0; i < map.Count; i++)
        {
            for (int j = 0; j < map[0].Count; j++)
            {
                if (map[i][j] == 0) // blocked
                    texture.SetPixel(i, j, new Color(1, 0, 0));
                else if (map[i][j] == 1) // open
                    texture.SetPixel(i, j, new Color(0, 0, 0));
                else
                    texture.SetPixel(i, j, new Color(0, 0, 1));
            }
        }

        //texture.SetPixel((int)mapper.GetMapCenter().x, (int)mapper.GetMapCenter().x, new Color(0, 0, 1)); // Current Pos
        //Vector2 targetPos = -mapper.GetMapTargetPos();
        //texture.SetPixel((int)targetPos.x, (int)targetPos.y, new Color(1, 1, 0));

        texture.Apply();
        RenderTexture.active = null; //don't forget to set it back to null once you finished playing with it. 
    }
}
