using UnityEngine;

/// <summary>
/// Stub of the EFT ModPlacer component (global namespace, compiled into the project's
/// Assembly-CSharp): when the bundle is loaded in game, Unity binds it to the real class by
/// (assembly, namespace, class). A mod model hung on its bone
/// (ContainerCollectionView.SlotView.InsertItem) takes these as its local pose instead of the
/// default (0, 0, 0) / (90, 0, 0); the Modular Vests client reads them as the pouch's mount on
/// its seat. Fields match the game 1:1.
/// </summary>
public class ModPlacer : MonoBehaviour
{
    public Vector3 ModPosition = Vector3.zero;
    public Vector3 ModRotation = Vector3.zero;
    public Vector3 ModScale = Vector3.one;
}
