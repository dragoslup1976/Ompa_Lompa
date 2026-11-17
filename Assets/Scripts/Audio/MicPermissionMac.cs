using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OmpaLompa.Audio
{
    public static class MicPermissionMac
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("MicPermission")] static extern int MicPermission_Status();
        [DllImport("MicPermission")] static extern void MicPermission_Request();

        public static int Status => MicPermission_Status();   // 0,1,2 sau 3
        public static void Request() => MicPermission_Request();
#else
        public static int Status => 3;
        public static void Request() { }
#endif

        public static IEnumerator EnsureAccess()
        {
            Debug.Log($"[MicPermission] status initial = {Status} (0=nedecis, 2=refuzat, 3=acordat)");
            if (Status == 0)
            {
                Request();
                float t = 0f;
                while (Status == 0 && t < 30f) { t += Time.unscaledDeltaTime; yield return null; }
            }
            Debug.Log($"[MicPermission] status final = {Status}");
        }
    }
}
