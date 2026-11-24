using UnityEngine;


/// Si la boîte est inclinée au-delà de pourAngle,
/// les particules démarrent ; sinon elles s'arrêtent.
/// Le script ne modifie jamais la rotation de la boîte, il la lit seulement.

public class BoxNourriture : MonoBehaviour
{
    [Tooltip("Ton système de particules. Désactive 'Play On Awake' dessus")]
    public ParticleSystem particules;

    [Tooltip("Angle (en degrés) par rapport à la verticale au-delà duquel on verse")]
    public float pourAngle = 60f;

    bool enCours;

    void Update()
    {
        // Angle entre le haut de la boîte et la verticale du monde (0° = droite, 90° = couchée)
        float angle = Vector3.Angle(transform.up, Vector3.up);

        bool doitVerser = angle > pourAngle;
        if (doitVerser == enCours) return;
        enCours = doitVerser;

        if (particules == null) return;

        if (enCours) particules.Play();
        else particules.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}