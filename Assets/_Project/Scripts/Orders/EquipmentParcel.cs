using UnityEngine;
using UnityEngine.Rendering;

namespace BORis
{
    // The parcel is presentation for a saved order. Its position never needs to be saved.
    sealed class EquipmentParcel
    {
        readonly Transform destination;
        readonly GameObject marker;
        readonly Material cardboard, tape, label, ink, highlight;
        Vector3 velocity;

        public Transform Root { get; }
        public BoxCollider Hitbox { get; }

        public EquipmentParcel(Transform window, Transform destination)
        {
            this.destination = destination;
            Root = new GameObject("SolderingStationParcel").transform;
            Root.SetParent(window, false);
            Hitbox = Root.gameObject.AddComponent<BoxCollider>();
            Hitbox.size = new Vector3(.46f, .30f, .35f);
            Hitbox.center = Vector3.up * .15f;

            cardboard = MakeMaterial("Parcel · kraft", new Color(.59f, .37f, .20f));
            tape = MakeMaterial("Parcel · ochre tape", new Color(.86f, .58f, .27f));
            label = MakeMaterial("Parcel · paper label", new Color(.85f, .86f, .76f));
            ink = MakeMaterial("Parcel · ink", new Color(.11f, .18f, .20f));
            highlight = MakeMaterial("Installation · amber", new Color(1f, .56f, .19f), true);
            Panel(Root, "Carton", Vector3.up * .15f, Hitbox.size, cardboard);
            Panel(Root, "Top tape", new Vector3(0, .302f, 0), new Vector3(.085f, .004f, .35f), tape);
            Panel(Root, "Front tape", new Vector3(0, .25f, -.177f), new Vector3(.085f, .1f, .004f), tape);
            Panel(Root, "Back tape", new Vector3(0, .25f, .177f), new Vector3(.085f, .1f, .004f), tape);
            Panel(Root, "Shipping label", new Vector3(0, .138f, -.18f), new Vector3(.36f, .15f, .004f), label);
            Panel(Root, "Stamp · contact", new Vector3(-.133f, .105f, -.184f), new Vector3(.051f, .013f, .004f), ink);
            Panel(Root, "Stamp · iron", new Vector3(-.119f, .142f, -.184f), new Vector3(.016f, .064f, .004f), ink)
                .localRotation = Quaternion.Euler(0, 0, -38);
            var print = new GameObject("Shipping label print").AddComponent<TextMesh>();
            print.transform.SetParent(Root, false);
            print.transform.localPosition = new Vector3(.034f, .14f, -.184f);
            print.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            print.GetComponent<MeshRenderer>().sharedMaterial = print.font.material;
            print.text = "Б.О.Р.И.С.  /  СНАБЖЕНИЕ\nПАЯЛЬНАЯ СТАНЦИЯ\n01   ▮▮ ▮ ▮▮▮";
            print.anchor = TextAnchor.MiddleCenter;
            print.alignment = TextAlignment.Center;
            print.fontSize = 45;
            print.characterSize = .005f;
            print.color = new Color(.10f, .17f, .20f);

            marker = new GameObject("SolderingStationInstallationOutline");
            marker.transform.SetPositionAndRotation(destination.position + Vector3.up * .035f, destination.rotation);
            marker.transform.SetParent(window, true);
            foreach (float side in new[] { -1f, 1f })
            {
                Panel(marker.transform, "Edge", new Vector3(side * .33f, 0, 0), new Vector3(.016f, .008f, .48f), highlight);
                Panel(marker.transform, "Edge", new Vector3(0, 0, side * .24f), new Vector3(.66f, .008f, .016f), highlight);
            }
            Hide();
        }

        static Material MakeMaterial(string name, Color color, bool lit = false)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            material.SetFloat("_Smoothness", .12f);
            if (lit)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * .55f);
            }
            return material;
        }

        static Transform Panel(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = position;
            panel.transform.localScale = scale;
            panel.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var renderer = panel.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (name != "Carton") renderer.shadowCastingMode = ShadowCastingMode.Off;
            return panel.transform;
        }

        public void SetOn(Transform support)
        {
            Root.SetPositionAndRotation(support.position, support.rotation);
            velocity = Vector3.zero;
            Root.gameObject.SetActive(true);
            Hitbox.enabled = true;
            marker.SetActive(false);
            Physics.SyncTransforms();
        }

        public void Follow(Camera view, int mask)
        {
            Quaternion yaw = Quaternion.Euler(0, view.transform.eulerAngles.y, 0);
            Vector3 offset = view.transform.rotation * new Vector3(.34f, -.62f, 1.10f);
            if (Physics.SphereCast(view.transform.position, .22f, offset.normalized, out RaycastHit wall,
                offset.magnitude, mask, QueryTriggerInteraction.Ignore))
                offset = offset.normalized * Mathf.Max(.23f, wall.distance - .04f);
            Root.position = Vector3.SmoothDamp(Root.position, view.transform.position + offset, ref velocity,
                .09f, Mathf.Infinity, Time.unscaledDeltaTime);
            Root.rotation = Quaternion.Slerp(Root.rotation, yaw, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        }

        public bool CanPlace(Ray ray, float reach, int mask)
        {
            var plane = new Plane(Vector3.up, marker.transform.position);
            if (!plane.Raycast(ray, out float distance) || distance > reach) return false;
            Vector3 point = marker.transform.InverseTransformPoint(ray.GetPoint(distance));
            return Mathf.Abs(point.x) <= .37f && Mathf.Abs(point.z) <= .28f
                && !Physics.Raycast(ray, Mathf.Max(0, distance - .04f), mask, QueryTriggerInteraction.Ignore);
        }

        public void Preview(Ray ray, float reach, int mask)
        {
            marker.SetActive(true);
            highlight.SetColor("_EmissionColor", new Color(1f, .56f, .19f) * (CanPlace(ray, reach, mask) ? 1.45f : .35f));
        }

        public void Hide()
        {
            Root.gameObject.SetActive(false);
            marker.SetActive(false);
        }

        public void Dispose()
        {
            Object.Destroy(cardboard);
            Object.Destroy(tape);
            Object.Destroy(label);
            Object.Destroy(ink);
            Object.Destroy(highlight);
        }
    }
}
