using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SpatialTracking;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace FieldInspection
{
    // Runtime composition keeps scene wiring reproducible and reviewable.
    // There is no simulated placement path: every marker requires an AR plane hit.
    public class InspectionApp : MonoBehaviour
    {
        ARPlaneManager planes;
        ARRaycastManager raycasts;
        InputField serverInput, titleInput, descriptionInput;
        Dropdown priorityInput;
        Button submit, clear;
        Text feedback, tracking;
        GameObject pendingMarker;
        Material markerMaterial;
        bool busy;
        Font font;
        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        readonly string[] priorityValues = { "low", "medium", "high" };

        void Awake()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            markerMaterial = Resources.Load<Material>("InspectionMarker");
            BuildAR();
            BuildUI();
        }

        void BuildAR()
        {
            var session = new GameObject("AR Session");
            session.AddComponent<ARSession>();
            session.AddComponent<ARInputManager>();

            var originObject = new GameObject("XR Origin");
            originObject.SetActive(false);
            var origin = originObject.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            origin.CameraFloorOffsetObject = offset;
            origin.CameraYOffset = 0f;
            var cameraObject = new GameObject("AR Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            origin.Camera = camera;
            cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();
            var pose = cameraObject.AddComponent<TrackedPoseDriver>();
            pose.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
            pose.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            pose.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            planes = originObject.AddComponent<ARPlaneManager>();
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            raycasts = originObject.AddComponent<ARRaycastManager>();
            originObject.SetActive(true);
        }

        void Update()
        {
            tracking.text = "AR: " + ARSession.state + "  |  Planes: " + planes.trackables.count;
            foreach (var plane in planes.trackables)
                if (plane.GetComponent<PlaneOutline>() == null) plane.gameObject.AddComponent<PlaneOutline>();
            if (busy || ARSession.state != ARSessionState.SessionTracking) return;

            // Check actual UI raycasts at the touch position. This also covers the
            // dropdown's expanded blocker and avoids EventSystem update-order races.
            if (Input.touchCount == 1)
            {
                var touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began) TryPlace(touch.position, touch.fingerId);
            }
#if UNITY_EDITOR
            if (Input.touchCount == 0 && Input.GetMouseButtonDown(0)) TryPlace(Input.mousePosition, -1);
#endif
        }

        void TryPlace(Vector2 point, int fingerId)
        {
            if (EventSystem.current == null) return;
            var pointer = new PointerEventData(EventSystem.current) { position = point, pointerId = fingerId };
            uiHits.Clear();
            EventSystem.current.RaycastAll(pointer, uiHits);
            if (uiHits.Count > 0) return;
            if (!raycasts.Raycast(point, hits, TrackableType.PlaneWithinPolygon))
            { Say("No plane hit. Move the phone slowly and tap a green outline.", true); return; }
            var plane = planes.GetPlane(hits[0].trackableId);
            if (plane == null || plane.trackingState != TrackingState.Tracking) return;
            var pose = hits[0].pose;
            if (pendingMarker == null)
            {
                pendingMarker = new GameObject("Pending issue marker");
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.transform.SetParent(pendingMarker.transform, false);
                mesh.transform.localPosition = Vector3.up * 0.05f;
                mesh.transform.localScale = Vector3.one * 0.1f;
                mesh.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                Destroy(mesh.GetComponent<Collider>());
            }
            // The marker pivot is the actual AR raycast hit. Offset only its mesh.
            pendingMarker.transform.SetPositionAndRotation(pose.position, pose.rotation);
            Say("Marker placed. Add a title and submit. Tap another plane to move it.");
        }

        void Submit()
        {
            if (busy) return;
            if (pendingMarker == null) { Say("Tap a detected plane to place a marker first.", true); return; }
            string title = titleInput.text.Trim();
            if (title.Length == 0) { Say("Title is required.", true); return; }
            string address = serverInput.text.Trim().TrimEnd('/');
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
            { Say("Enter a valid backend URL, e.g. http://192.168.1.20:8080", true); return; }
            var p = pendingMarker.transform.position;
            var data = new CreateIssue {
                title = title, description = descriptionInput.text.Trim(),
                priority = priorityValues[priorityInput.value],
                position = new PositionData { x = p.x, y = p.y, z = p.z }
            };
            PlayerPrefs.SetString("BackendUrl", address);
            PlayerPrefs.Save();
            StartCoroutine(Send(address, data));
        }

        IEnumerator Send(string address, CreateIssue data)
        {
            SetBusy(true);
            Say("Submitting issue...");
            using (var request = new UnityWebRequest(address + "/api/issues", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Issue saved = null;
                    try { saved = JsonUtility.FromJson<Issue>(request.downloadHandler.text); } catch (ArgumentException) { }
                    if (saved == null || string.IsNullOrEmpty(saved.id))
                    { Say("Invalid server response. Check the admin page before retrying.", true); }
                    else
                    {
                        pendingMarker.name = "Issue " + saved.id;
                        var material = new Material(markerMaterial);
                        material.color = data.priority == "high" ? new Color(1f,0.35f,0.25f) :
                            data.priority == "medium" ? new Color(1f,0.75f,0.2f) : new Color(0.35f,0.8f,0.5f);
                        pendingMarker.GetComponentInChildren<Renderer>().sharedMaterial = material;
                        pendingMarker = null;
                        titleInput.text = ""; descriptionInput.text = "";
                        Say("Submitted! ID: " + saved.id.Substring(0, Math.Min(8, saved.id.Length)) + ". Refresh the admin page.");
                    }
                }
                else
                {
                    string detail = request.error;
                    try {
                        var error = JsonUtility.FromJson<ApiError>(request.downloadHandler.text);
                        if (error != null && error.error != null) detail = error.error.message;
                    } catch (ArgumentException) { }
                    Say("Submit failed: " + detail + ". Form kept. If timed out, check admin before retrying.", true);
                }
            }
            SetBusy(false);
        }

        void SetBusy(bool value)
        {
            busy = value;
            submit.interactable = clear.interactable = !value;
            serverInput.interactable = titleInput.interactable = descriptionInput.interactable = priorityInput.interactable = !value;
        }
        void Say(string message, bool error = false) { feedback.text = message; feedback.color = error ? new Color(1f,0.64f,0.5f) : new Color(0.75f,0.94f,0.76f); }

        void BuildUI()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvasObject = new GameObject("Inspection UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.matchWidthOrHeight = 0.5f;
            // Keep all controls within the device safe area (notch / navigation bar).
            var safe = Rect("Safe Area", canvasObject.transform, Vector2.zero, Vector2.one);
            var area = Screen.safeArea;
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            var top = Rect("Tracking header", safe, new Vector2(0,0.87f), Vector2.one);
            top.gameObject.AddComponent<Image>().color = new Color(0.06f,0.14f,0.13f,0.92f);
            Label(top, "FIELDNOTE / AR INSPECTION", new Vector2(.04f,.55f), new Vector2(.96f,.95f), 25);
            tracking = Label(top,"Starting AR...",new Vector2(.04f,.05f),new Vector2(.96f,.5f),20);
            var panel = Rect("Issue form", safe, Vector2.zero, new Vector2(1,.49f));
            panel.gameObject.AddComponent<Image>().color = new Color(.06f,.14f,.13f,.96f);
            Label(panel,"BACKEND URL (computer LAN IP)",new Vector2(.04f,.91f),new Vector2(.96f,.98f),19);
            serverInput = Field(panel,"http://192.168.1.20:8080",.81f,.91f,120);
            serverInput.text = PlayerPrefs.GetString("BackendUrl", "http://192.168.1.20:8080");
            Label(panel,"TITLE *",new Vector2(.04f,.74f),new Vector2(.96f,.81f),19);
            titleInput = Field(panel,"Describe the problem (required)",.64f,.74f,120);
            descriptionInput = Field(panel,"Description (optional)",.49f,.62f,2000);
            descriptionInput.lineType = InputField.LineType.MultiLineNewline;
            priorityInput = PriorityDropdown(panel);
            submit = ActionButton(panel,"Submit issue",new Vector2(.04f,.24f),new Vector2(.65f,.35f),Submit);
            clear = ActionButton(panel,"Clear",new Vector2(.68f,.24f),new Vector2(.96f,.35f),() => {
                if (pendingMarker != null) Destroy(pendingMarker);
                pendingMarker = null;
                Say("Pending marker cleared. Tap a plane to place a new marker.");
            });
            feedback = Label(panel,"Move your phone slowly, then tap a detected horizontal plane.",new Vector2(.04f,.02f),new Vector2(.96f,.22f),21);
            feedback.alignment = TextAnchor.UpperLeft;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent,false); r.anchorMin = min; r.anchorMax = max; r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }
        Text Label(Transform parent, string value, Vector2 min, Vector2 max, int size)
        {
            var text = Rect("Label",parent,min,max).gameObject.AddComponent<Text>();
            text.font = font; text.text = value; text.fontSize = size; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            return text;
        }
        InputField Field(Transform parent, string hint, float minY, float maxY, int limit)
        {
            var rect = Rect("Input",parent,new Vector2(.04f,minY),new Vector2(.96f,maxY));
            rect.gameObject.AddComponent<Image>().color = new Color(.94f,.96f,.93f);
            var field = rect.gameObject.AddComponent<InputField>();
            var text = Label(rect,"",new Vector2(.02f,.08f),new Vector2(.98f,.92f),23);
            text.color = new Color(.12f,.22f,.17f);
            var placeholder = Label(rect,hint,new Vector2(.02f,.08f),new Vector2(.98f,.92f),21);
            placeholder.color = new Color(.45f,.53f,.46f);
            field.textComponent = text; field.placeholder = placeholder; field.characterLimit = limit;
            field.targetGraphic = rect.GetComponent<Image>();
            return field;
        }
        Button ActionButton(Transform parent,string value,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(value,parent,min,max);
            rect.gameObject.AddComponent<Image>().color = new Color(.28f,.48f,.34f);
            var button = rect.gameObject.AddComponent<Button>();
            var label = Label(rect,value,Vector2.zero,Vector2.one,24); label.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(action); return button;
        }
        Dropdown PriorityDropdown(Transform parent)
        {
            var root = Rect("Priority",parent,new Vector2(.04f,.38f),new Vector2(.96f,.47f));
            root.gameObject.AddComponent<Image>().color = new Color(.94f,.96f,.93f);
            var dropdown = root.gameObject.AddComponent<Dropdown>();
            var caption = Label(root,"",new Vector2(.02f,0),new Vector2(.98f,1),23); caption.color = new Color(.12f,.22f,.17f);
            dropdown.captionText = caption;
            var template = Rect("Template",root,new Vector2(0,0),new Vector2(1,0));
            template.pivot = new Vector2(.5f,1); template.sizeDelta = new Vector2(0,165);
            template.gameObject.AddComponent<Image>().color = new Color(.94f,.96f,.93f);
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = Rect("Viewport",template,Vector2.zero,Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content",viewport,new Vector2(0,1),Vector2.one);
            content.pivot = new Vector2(.5f,1); content.sizeDelta = new Vector2(0,55);
            var item = Rect("Item",content,new Vector2(0,.5f),new Vector2(1,.5f)); item.sizeDelta = new Vector2(0,55);
            var background = item.gameObject.AddComponent<Image>(); background.color = new Color(.8f,.88f,.8f);
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = background;
            var check = Label(item,"✓",new Vector2(.02f,0),new Vector2(.09f,1),23); check.color = Color.black; toggle.graphic = check;
            var itemText = Label(item,"",new Vector2(.1f,0),new Vector2(.98f,1),23); itemText.color = new Color(.12f,.22f,.17f);
            scroll.viewport = viewport; scroll.content = content;
            dropdown.template = template; dropdown.itemText = itemText;
            dropdown.AddOptions(new List<string> { "Low priority", "Medium priority", "High priority" });
            dropdown.value = 1; dropdown.RefreshShownValue(); template.gameObject.SetActive(false);
            return dropdown;
        }
    }
}
