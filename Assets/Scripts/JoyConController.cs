
using UnityEngine;
using extOSC;

public class JoyConController : MonoBehaviour
{
    public OSCReceiver receiver;

    [Header("Accelerometer")]
    public Vector3 acceleration;

    //Seuil de détection
    [Header("Detection")]
    public float threshold = -3000f;

    public bool pointingDown;

    private bool previousState;

    void Start()
    {
        receiver.Bind("/accel", OnAcceleration);
    }

    void OnAcceleration(OSCMessage message)
    {
        //Si je reçois pas x y et z 
        if (message.Values.Count < 3)
            return;

        acceleration = new Vector3(
            message.Values[0].IntValue,
            message.Values[1].IntValue,
            message.Values[2].IntValue
        );

        /*
        Debug.Log("X : " + acceleration.x); 
        Debug.Log("Y : " + acceleration.y);
        Debug.Log("Z : " + acceleration.z);
        */

        // Détection de la position vers le bas
        pointingDown = acceleration.x < threshold;

        // Déclenchement uniquement au changement d'état
        if (pointingDown && !previousState)
        {
            Debug.Log("JOYCON POINTE VERS LE BAS !");
        }

        if (!pointingDown && previousState)
        {
            Debug.Log("JOYCON N'EST PLUS VERS LE BAS");
        }

        previousState = pointingDown;
    }
}
