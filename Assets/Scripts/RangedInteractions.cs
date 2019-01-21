﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Util;
using VRTK;

public class RangedInteractions : VRTK_StraightPointerRenderer {

    public GameObject screen;

    [Header("Sprite Parameters")]
    [SerializeField] [Tooltip("The sprite renderer that renders the sprites for the selected ranged tool.")]
    private SpriteRenderer selectedInteractionRenderer;
    [SerializeField] [Tooltip("The sprite used to represent the ranged brush tool.")]
    private Sprite rangedBrushSprite;
    [SerializeField] [Tooltip("The sprite used to represent the lasso selection tool.")]
    private Sprite lassoSelectionSprite;
    [SerializeField] [Tooltip("The sprite used to represent the rectangle selection tool.")]
    private Sprite rectangleSelectionSprite;
    [SerializeField] [Tooltip("The sprite used to represent the ranged interaction tool.")]
    private Sprite rangedInteractionSprite;

    [Header("Ranged Brush Parameters")]
    [SerializeField] [Tooltip("The prefab of the brush to use.")]
    private GameObject brushPrefab;
    [SerializeField] [Tooltip("The factor which affects the rate that the brush is resized.")]
    private float rangedBrushScaleFactor = 0.5f;
    [SerializeField] [Tooltip("The minimum size of the ranged brush.")]
    private float rangedBrushMin = 0.01f;
    [SerializeField] [Tooltip("The maximum size of the ranged brush.")]
    private float rangedBrushMax = 0.1f;

    private GameObject rangedBrush;
    private float angle;

    [Header("Lasso Selection Parameters")]
    [SerializeField] [Tooltip("The width of the line drawn while lassoing.")]
    private float lassoWidth = 0.005f;
    [SerializeField] [Tooltip("The color of the line drawn while lassoing.")]
    private Color lassoDrawColor = new Color(255, 255, 255);
    [SerializeField] [Tooltip("The color of the line drawn when the user completes the lasso.")]
    private Color lassoCompleteColor = new Color(255, 255, 0);
    [SerializeField] [Tooltip("The material of the line darwn while lassoing.")]
    private Material lassoMaterial;
    [SerializeField] [Tooltip("The initial distance the user has to move the controller before a lasso selection is formable.")]
    private float lassoPointInitialDistance = 0.05f;
    [SerializeField] [Tooltip("The distance that the user has to move the controller before another point is added to the line.")]
    private float lassoPointDistanceInterval = 0.005f;
    [SerializeField] [Tooltip("The distance from the start point that the end point has to be for it to be registered as a lasso selection.")]
    private float lassoPointCompleteDistance = 0.015f;
    [SerializeField] [Tooltip("The script used to draw convex meshes of selections.")]
    public ConvexMesh convexMesh;

    private LineRenderer lassoRenderer;
    private bool isLassoPastInitialDistance = false;
    private bool isLassoComplete = false;

    [Header("Rectangle Selection Parameters")]
    [SerializeField] [Tooltip("The width of the line drawn while selecting.")]
    private float rectangleSelectWidth = 0.01f;
    [SerializeField] [Tooltip("The material of the rectangle while selectiong.")]
    private Material rectangleSelectMaterial;

    private Vector3 rectangleStart;
    private Vector3 rectangleEnd;
    private GameObject selectionSquare;

    [Header("Ranged Selection Parameters")]
    [SerializeField] [Tooltip("The distance that the controller needs to be moved until an object begins being pulled from the screen.")]
    private float rangedPullStartThreshold = 0.025f;
    [SerializeField] [Tooltip("The distance that the controller needs to be moved until an object finishes being pulled from the screen.")]
    private float rangedPullCompleteThreshold = 0.2f;

    private Vector3 rangedPullControllerStartPosition;
    private Vector3 rangedPullObjectStartPosition;
    private GameObject rangedPullGameObject;
    private bool isPullable = false;

    [Header("Details on Demand Parameters")]
    [SerializeField] [Tooltip("The gameobject that acts as the panel for the details on demand.")]
    private GameObject detailsOnDemandGameObject;
    [SerializeField] [Tooltip("The textmesh which displays the details on demand.")]
    private TextMeshPro detailsOnDemandTextMesh;

    private bool isTouchpadDown = false;
    private int previousInspectedIndex;

    private VRTK_ControllerEvents controllerEvents;
    //private GameObject screen;
    //private GameObject chart;

    private InteractionState activeState = InteractionState.None;
    private InteractionState previousState = InteractionState.None;
    private InteractionState hiddenState = InteractionState.None;

    private SelectionMode selectionMode = SelectionMode.None;

    private bool isEnabled = false;
    private bool isControllerSelecting = true;

    /// <summary>
    /// The state of interaction the user is currently in. Note that this scope only extends to that of touchpad interactions, and not
    /// to other forms of interaction
    /// </summary>
    private enum InteractionState
    {
        None,
        RangedBrush,
        LassoSelection,
        RectangleSelection,
        RangedInteraction,
        RangedBrushing,
        LassoSelecting,
        RectangleSelecting,
        RangedInteracting,
        RangedPulling,
        DetailsOnDemand
    }

    private enum SelectionMode
    {
        None,
        Selecting,
        Deselecting
    }

    public string ActiveState
    {
        get { return activeState.ToString(); }
    }

    public bool IsSelecting
    {
        get { return selectionMode == SelectionMode.Selecting; }
    }

    public bool IsDeselecting
    {
        get { return selectionMode == SelectionMode.Deselecting; }
    }

    private void Start()
    {
        controllerEvents = GetComponent<VRTK_ControllerEvents>();
        controllerEvents.TouchpadPressed += OnTouchpadStart;
        controllerEvents.TouchpadAxisChanged += OnTouchpadAxisChange;
        controllerEvents.TouchpadReleased += OnTouchpadEnd;
        controllerEvents.TriggerClicked += OnTriggerStart;
        controllerEvents.TriggerUnclicked += OnTriggerEnd;
        controllerEvents.GripClicked += OnGripStart;
        controllerEvents.GripUnclicked += OnGripEnd;

        // Instantiate ranged brush
        rangedBrush = Instantiate(brushPrefab);
        rangedBrush.SetActive(false);
    }
    
    public void Enable()
    {
        isEnabled = true;
    }

    public void Disable()
    {
        isEnabled = false;

        if (!IsInteracting())
        {
            SetInteractionState(InteractionState.None);
        }
    }

    public void Hide()
    {
        hiddenState = activeState;
        SetInteractionState(InteractionState.None);
    }

    public void Show()
    {
        SetInteractionState(hiddenState);
        hiddenState = InteractionState.None;
    }

    /// <summary>
    /// Changes the interaction tool that is used on the controller. This is designed to be called by functions outside of this script.
    /// </summary>
    /// <param name="interactionType"></param>
    public void InteractionToolChanged(string interactionType)
    {
        // Only change the tool that is used if the active state is a default one
        if (new string[] { "none", "rangedbrush", "lassoselection", "rectangleselection", "rangedinteraction" }.Contains(activeState.ToString().ToLower()))
        {
            switch (interactionType.ToLower())
            {
                case "none":
                    SetInteractionState(InteractionState.None);
                    break;

                case "rangedbrush":
                    SetInteractionState(InteractionState.RangedBrush);
                    break;

                case "lassoselection":
                    SetInteractionState(InteractionState.LassoSelection);
                    break;

                case "rectangleselection":
                    SetInteractionState(InteractionState.RectangleSelection);
                    break;

                case "rangedinteraction":
                    SetInteractionState(InteractionState.RangedInteraction);
                    break;
            }
        }
        // Otherwise this would've interrupted a user's active interaction, therefore vibrate hard to warn them of this
        else
        {
            VRTK_ControllerHaptics.TriggerHapticPulse(VRTK_ControllerReference.GetControllerReference(gameObject), 0.75f, 0.05f, 0.005f);
        }
    }

    /// <summary>
    /// This method changes the InteractionState to the one specified. Note that this does not check for any pre-conditions before switching the state
    /// and should only be called where allowed.
    /// </summary>
    /// <param name="state">The InteractionState to change to</param>
    private void SetInteractionState(InteractionState state)
    {
        // If ranged interactions are disabled, don't allow any interaction changes (mainly when finishing an existing interaction)
        if (!isEnabled)
            state = InteractionState.None;

        switch (state)
        {
            case InteractionState.None:
                previousState = InteractionState.None;
                selectedInteractionRenderer.sprite = null;
                tracerVisibility = VisibilityStates.AlwaysOff;
                selectionMode = SelectionMode.None;
                break;

            case InteractionState.RangedBrush:
                selectedInteractionRenderer.sprite = rangedBrushSprite;
                tracerVisibility = VisibilityStates.AlwaysOn;
                break;

            case InteractionState.RangedBrushing:
                break;

            case InteractionState.LassoSelection:
                selectedInteractionRenderer.sprite = lassoSelectionSprite;
                tracerVisibility = VisibilityStates.AlwaysOn;
                break;

            case InteractionState.LassoSelecting:
                break;

            case InteractionState.RectangleSelection:
                selectedInteractionRenderer.sprite = rectangleSelectionSprite;
                tracerVisibility = VisibilityStates.AlwaysOn;
                break;

            case InteractionState.RectangleSelecting:
                break;

            case InteractionState.RangedInteraction:
                selectedInteractionRenderer.sprite = rangedInteractionSprite;
                tracerVisibility = VisibilityStates.AlwaysOn;
                break;

            case InteractionState.RangedInteracting:
                break;

            case InteractionState.RangedPulling:
                break;
        }

        activeState = state;
        Debug.Log("Ranged interaction state changed to " + state.ToString());
    }

    /// <summary>
    /// Checks to see if there is an interaction tool that is selected.
    /// </summary>
    /// <returns>True if there is currently an interaction tool selected, otherwise returns false</returns>
    private bool IsInteractionToolActive()
    {
        return (activeState != InteractionState.None);
    }

    /// <summary>
    /// Checks to see if there is an ongoing interaction. Note that this is specifically for when the user is performing an interaction, not when they only have it selected.
    /// </summary>
    /// <returns>True if the user is currently performing an interaction, otherwise returns false</returns>
    private bool IsInteracting()
    {
        return new string[] { "rangedbrushing", "lassoselecting", "rectangleselecting", "rangedinteracting", "rangedpulling" }.Contains(activeState.ToString().ToLower());
    }

    /// <summary>
    /// Called when the user presses the trigger enough until it clicks. This will call the respective function depending on which
    /// interaction tool was originally selected by the user.
    /// </summary>
    private void OnTriggerStart(object sender, ControllerInteractionEventArgs e)
    {
        if (IsInteractionToolActive() && !IsInteracting())
        {
            selectionMode = SelectionMode.Selecting;

            InteractionsManager.Instance.RangedInteractionStarted();

            RaycastHit pointerCollidedWith;
            GameObject collidedObject = GetCollidedObject(out pointerCollidedWith);

            if (collidedObject != null && (collidedObject.tag == "HtmlElement" || collidedObject.tag == "HtmlAxisElement"))
            {
                previousState = activeState;
                RangedInteractionTriggerStart(e);
            }
            else
            {
                switch (activeState)
                {
                    case InteractionState.RangedBrush:
                        RangedBrushTriggerStart(e);
                        break;

                    case InteractionState.LassoSelection:
                        LassoSelectionTriggerStart(e);
                        break;

                    case InteractionState.RectangleSelection:
                        RectangleSelectionTriggerStart(e);
                        break;

                    case InteractionState.RangedInteraction:
                        RangedInteractionTriggerStart(e);
                        break;
                }
            }
        }
    }

    private void OnTriggerEnd(object sender, ControllerInteractionEventArgs e)
    {
        if (IsInteractionToolActive() && IsSelecting)
        {
            InteractionsManager.Instance.RangedInteractionFinished();

            switch (activeState)
            {
                case InteractionState.RangedBrushing:
                    RangedBrushTriggerEnd(e);
                    break;

                case InteractionState.LassoSelecting:
                    LassoSelectionTriggerEnd(e);
                    break;

                case InteractionState.RectangleSelecting:
                    RectangleSelectionTriggerEnd(e);
                    break;

                case InteractionState.RangedInteracting:
                    RangedInteractionTriggerEnd(e);
                    break;

                case InteractionState.RangedPulling:
                    RangedPullTriggerEnd(e);
                    break;
            }
        }
    }

    private void OnGripStart(object sender, ControllerInteractionEventArgs e)
    {
        if (IsInteractionToolActive() && !IsInteracting())
        {
            selectionMode = SelectionMode.Deselecting;

            InteractionsManager.Instance.RangedInteractionStarted();
            
            switch (activeState)
            {
                case InteractionState.RangedBrush:
                    RangedBrushTriggerStart(e);
                    break;

                case InteractionState.LassoSelection:
                    LassoSelectionTriggerStart(e);
                    break;

                case InteractionState.RectangleSelection:
                    RectangleSelectionTriggerStart(e);
                    break;
            }
        }
    }

    private void OnGripEnd(object sender, ControllerInteractionEventArgs e)
    {
        if (IsInteractionToolActive() && IsDeselecting)
        {
            InteractionsManager.Instance.RangedInteractionFinished();

            switch (activeState)
            {
                case InteractionState.RangedBrushing:
                    RangedBrushTriggerEnd(e);
                    break;

                case InteractionState.LassoSelecting:
                    LassoSelectionTriggerEnd(e);
                    break;

                case InteractionState.RectangleSelecting:
                    RectangleSelectionTriggerEnd(e);
                    break;

                case InteractionState.RangedInteracting:
                    RangedInteractionTriggerEnd(e);
                    break;

                case InteractionState.RangedPulling:
                    RangedPullTriggerEnd(e);
                    break;
            }
        }
    }

    private void OnTouchpadStart(object sender, ControllerInteractionEventArgs e)
    {
        angle = e.touchpadAngle;

        isTouchpadDown = true;
        previousInspectedIndex = -1;
    }

    private void OnTouchpadAxisChange(object sender, ControllerInteractionEventArgs e)
    {
        if (activeState == InteractionState.RangedBrushing)
        {
            float delta = e.touchpadAngle - angle;
            // Instance where touch crosses over from 0 to 359 degrees
            if (delta >= 180)
                delta = -(360 - delta);
            // Instance where touch crosses over from 359 to 0 degrees
            else if (delta < -180)
                delta = 360 + delta;

            float newCursorScaleMultiplier = cursorScaleMultiplier + delta * rangedBrushScaleFactor;
            Vector3 currentScale = rangedBrush.transform.localScale;
            rangedBrush.transform.localScale = Vector3.one * (scaleFactor * newCursorScaleMultiplier);

            float newSize = rangedBrush.transform.localScale.x;

            if (newSize >= rangedBrushMin && newSize <= rangedBrushMax)
                cursorScaleMultiplier = newCursorScaleMultiplier;
            else
                rangedBrush.transform.localScale = currentScale;

            angle = e.touchpadAngle;
        }
    }

    private void OnTouchpadEnd(object sender, ControllerInteractionEventArgs e)
    {
        isTouchpadDown = false;
    }

    /* DETAILS ON DEMAND
    private void Update()
    {
        if (activeState == InteractionState.None && !wandController.IsDragging() && isTouchpadDown)
        {
            tracerVisibility = VisibilityStates.AlwaysOn;

            RaycastHit hit;
            GameObject collidedObject = GetCollidedObject(out hit);

            if (collidedObject != null && collidedObject.tag == "Shape")
            {
                if (!detailsOnDemandGameObject.activeSelf)
                    detailsOnDemandGameObject.SetActive(true);

                // If this shape is not the currently displayed one
                int index = collidedObject.GetComponent<InteractableShape>().Index;
                if (index != previousInspectedIndex)
                {
                    DataBinding.DataObject dataObject = SceneManager.Instance.dataObject;

                    List<string> values = new List<string>();
                    values.Add("Index: " + index);

                    for (int i = 0; i < dataObject.NbDimensions; i++)
                    {
                        string name = dataObject.Identifiers[i];
                        string value = dataObject.getRawValue(i, index);

                        values.Add(string.Format("{0}: {1}", name, value));
                    }

                    detailsOnDemandTextMesh.text = string.Join("\n", values);
                    previousInspectedIndex = index;
                }
            }
        }
        else
        {
            if (activeState == InteractionState.None && (wandController.IsDragging() || !isTouchpadDown))
            {
                tracerVisibility = VisibilityStates.AlwaysOff;
                if (detailsOnDemandGameObject.activeSelf)
                {
                    detailsOnDemandGameObject.SetActive(false);
                }
            }
        }
    }
    */

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        switch (activeState)
        {
            case InteractionState.RangedBrushing:
                RangedBrushLoop();
                break;

            case InteractionState.LassoSelecting:
                LassoSelectionLoop();
                break;

            case InteractionState.RectangleSelecting:
                RectangleSelectionLoop();
                break;

            case InteractionState.RangedInteracting:
                RangedInteractionLoop();
                break;

            case InteractionState.RangedPulling:
                RangedPullLoop();
                break;
        }
    }

    private void RangedBrushTriggerStart(ControllerInteractionEventArgs e)
    {
        SetInteractionState(InteractionState.RangedBrushing);

        if (IsSelecting)
            rangedBrush.GetComponent<Brush>().SetBrushMode(Brush.BrushMode.Select);
        else if (IsDeselecting)
            rangedBrush.GetComponent<Brush>().SetBrushMode(Brush.BrushMode.Deselect);
    }

    private void RangedBrushLoop()
    {
        RaycastHit hit;
        GameObject collidedObject = GetCollidedObject(out hit);
        if (collidedObject != null)
        {
            if (!rangedBrush.activeSelf)
                rangedBrush.SetActive(true);

            /* TODO
            // Select all shapes between the current position and the new position to prevent jitteriness
            Vector3 start = rangedBrush.transform.position;
            Vector3 end = hit.point;
            Vector3 direction = Vector3.Normalize(end - start);

            RaycastHit[] raycastHits = Physics.SphereCastAll(start, rangedBrush.transform.localScale.x / 2, direction, Vector3.Distance(start, end));
            List<int> indicesToSelect = new List<int>();
            foreach (RaycastHit raycastHit in raycastHits)
            {
                if (raycastHit.collider.gameObject.tag == "Shape")
                {
                    InteractableShape shapeScript = raycastHit.collider.gameObject.GetComponent<InteractableShape>();
                    // If the brush is selecting and the shape is not selected, OR if the brush is deselecting and the shape is already selected
                    if ((IsSelecting && !shapeScript.IsSelected) || (IsDeselecting && shapeScript.IsSelected))
                        indicesToSelect.Add(shapeScript.Index);
                }
            }

            if (indicesToSelect.Count > 0)
            {
                VRTK_ControllerHaptics.TriggerHapticPulse(VRTK_ControllerReference.GetControllerReference(gameObject), 0.15f);

                if (IsSelecting)
                    ScreenManager.Instance.ShapesSelected(indicesToSelect.ToArray());
                if (IsDeselecting)
                    ScreenManager.Instance.ShapesDeselected(indicesToSelect.ToArray());
            }
            */
            rangedBrush.transform.position = hit.point;
        }
        else
        {
            if (rangedBrush.activeSelf)
                rangedBrush.SetActive(false);
        }

        if (IsValidCollision())
            rangedBrush.GetComponent<Renderer>().material.color = validCollisionColor;
        else
            rangedBrush.GetComponent<Renderer>().material.color = invalidCollisionColor;
    }

    private void RangedBrushTriggerEnd(ControllerInteractionEventArgs e)
    {
        SetInteractionState(InteractionState.RangedBrush);

        rangedBrush.SetActive(false);
    }

    private void LassoSelectionTriggerStart(ControllerInteractionEventArgs e)
    {
        SetInteractionState(InteractionState.LassoSelecting);

        lassoRenderer = gameObject.AddComponent<LineRenderer>();
        lassoRenderer.useWorldSpace = true;
        // Remove any anomalous default points
        lassoRenderer.positionCount = 0;
        lassoRenderer.startWidth = lassoWidth;
        lassoRenderer.endWidth = lassoWidth;
        lassoRenderer.material = lassoMaterial;
        lassoRenderer.material.color = lassoDrawColor;
        isLassoPastInitialDistance = false;
        isLassoComplete = false;

        convexMesh.SetIncomplete();
    }

    private void LassoSelectionLoop()
    {
        if (!isLassoComplete)
        {
            RaycastHit pointerCollidedWith;
            GameObject collidedObject = GetCollidedObject(out pointerCollidedWith);

            if (collidedObject != null)
            {
                if (collidedObject == screen || collidedObject.tag == "DisplayScreen")
                {
                    int nbLassoPoints = lassoRenderer.positionCount;

                    // If the point is far away enough from the previous position
                    if (nbLassoPoints == 0 || Vector3.Distance(pointerCollidedWith.point, lassoRenderer.GetPosition(nbLassoPoints - 1)) >= lassoPointDistanceInterval)
                    {
                        lassoRenderer.positionCount = nbLassoPoints + 1;
                        lassoRenderer.SetPosition(nbLassoPoints, pointerCollidedWith.point);
                        VRTK_ControllerHaptics.TriggerHapticPulse(VRTK_ControllerReference.GetControllerReference(gameObject), 0.05f);

                        // Update the mesh which shows the area that will be selected
                        Vector3[] points = new Vector3[lassoRenderer.positionCount];
                        lassoRenderer.GetPositions(points);
                        Vector3[] points3d = points.Select(p => screen.transform.InverseTransformPoint(p)).ToArray();
                        convexMesh.CreateConvexMesh(points3d);
                    }

                    // If the lasso has not already been moved beyond the initial distance necessary to complete a lasso, check to see if it has
                    if (!isLassoPastInitialDistance)
                    {
                        if (Vector3.Distance(pointerCollidedWith.point, lassoRenderer.GetPosition(0)) > lassoPointInitialDistance)
                            isLassoPastInitialDistance = true;
                    }
                    else
                    {
                        // If it has already been moved past its initial distance, check to see if it has come back and completed the lasso
                        if (Vector3.Distance(pointerCollidedWith.point, lassoRenderer.GetPosition(0)) <= lassoPointCompleteDistance)
                        {
                            isLassoComplete = true;
                            lassoRenderer.material.color = lassoCompleteColor;
                            convexMesh.SetComplete();
                        }
                    }
                }
            }
        }
    }

    private void LassoSelectionTriggerEnd(ControllerInteractionEventArgs e)
    {
        SetInteractionState(InteractionState.LassoSelection);

        if (isLassoComplete)
        {
            /* TODO
            List<int> indicesToSelect = new List<int>();

            int nbLassoPoints = lassoRenderer.positionCount;
            Vector3[] lassoWorldSpace = new Vector3[nbLassoPoints];
            lassoRenderer.GetPositions(lassoWorldSpace);
            Vector2[] lassoLocalSpace = lassoWorldSpace.Select(p => (Vector2)screen.transform.InverseTransformPoint(p)).ToArray();

            foreach (GameObject shape in ScreenManager.Instance.Shapes)
            {
                Vector2 point = screen.transform.InverseTransformPoint(shape.transform.position);
                if (ContainsPoint(lassoLocalSpace, point))
                {
                    indicesToSelect.Add(shape.GetComponent<InteractableShape>().Index);
                }

            }

            // Select any shapes which the line is touching
            foreach (Vector3 point in lassoWorldSpace)
            {
                Collider[] colliders = Physics.OverlapSphere(point, 0.01f);

                foreach (Collider collider in colliders)
                {
                    if (collider.gameObject.tag == "Shape")
                    {
                        indicesToSelect.Add(collider.gameObject.GetComponent<InteractableShape>().Index);
                    }
                }
            }

            if (IsSelecting)
                ScreenManager.Instance.ShapesSelected(indicesToSelect.ToArray());
            else if (IsDeselecting)
                ScreenManager.Instance.ShapesDeselected(indicesToSelect.ToArray());
            */
        }

        convexMesh.DestroyConvexMesh();
        Destroy(lassoRenderer);
    }

    private void RectangleSelectionTriggerStart(ControllerInteractionEventArgs e)
    {
        RaycastHit pointerCollidedWith;
        GameObject collidedObject = GetCollidedObject(out pointerCollidedWith);

        if (collidedObject != null)
        {
            if (collidedObject == screen || collidedObject.tag == "Shape")
            {
                SetInteractionState(InteractionState.RectangleSelecting);
                rectangleStart = pointerCollidedWith.point;
            }
        }
    }

    private void RectangleSelectionLoop()
    {
        RaycastHit pointerCollidedWith;
        GameObject collidedObject = GetCollidedObject(out pointerCollidedWith);

        if (collidedObject != null)
        {
            // Only draw the shape if the pointer is targeting at the screen
            if (collidedObject == screen)
            {
                // Create the square to be used for the selection if it does not already exist
                if (selectionSquare == null)
                {
                    selectionSquare = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    // Set it to ignore raycasts
                    selectionSquare.layer = 2;
                    selectionSquare.GetComponent<Renderer>().material = rectangleSelectMaterial;
                    selectionSquare.transform.SetParent(screen.transform);
                    LineRenderer lr = selectionSquare.AddComponent<LineRenderer>();
                    lr.positionCount = 5;
                    lr.SetPositions(new Vector3[] { new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0) });
                    lr.material = rectangleSelectMaterial;
                    lr.startWidth = rectangleSelectWidth;
                    lr.endWidth = rectangleSelectWidth;
                    lr.useWorldSpace = false;
                }

                rectangleEnd = pointerCollidedWith.point;

                Vector3 localStart = screen.transform.InverseTransformPoint(rectangleStart);
                Vector3 localEnd = screen.transform.InverseTransformPoint(rectangleEnd);

                Vector3 localScale = localStart - localEnd;
                localScale.x = Mathf.Abs(localScale.x);
                localScale.y = Mathf.Abs(localScale.y);
                localScale.z = Mathf.Abs(localScale.z) + 0.02f;  // Add a bit extra thickness to prevent Z-fighting
                Vector3 localCenter = (localStart + localEnd) / 2;

                selectionSquare.transform.localPosition = localCenter;
                selectionSquare.transform.localScale = localScale;
                selectionSquare.transform.rotation = screen.transform.rotation;
            }
        }
    }

    private void RectangleSelectionTriggerEnd(ControllerInteractionEventArgs e)
    {
        SetInteractionState(InteractionState.RectangleSelection);

        /* TODO
        selectionSquare.transform.parent = null;
        // Increase the area where the selection is done so that it catches shapes hovering away from the screen
        Vector3 halfExtents = selectionSquare.transform.localScale / 2;
        halfExtents.z += 0.3f;

        Collider[] colliders = Physics.OverlapBox(selectionSquare.transform.position, halfExtents, selectionSquare.transform.rotation);
        List<int> indices = new List<int>();

        foreach (Collider collider in colliders)
        {
            if (collider.gameObject.tag == "Shape")
            {
                InteractableShape shapeScript = collider.gameObject.GetComponent<InteractableShape>();
                indices.Add(shapeScript.Index);
            }
        }

        if (IsSelecting)
            ScreenManager.Instance.ShapesSelected(indices.ToArray());
        else if (IsDeselecting)
            ScreenManager.Instance.ShapesDeselected(indices.ToArray());
        */
        Destroy(selectionSquare);
    }

    private void RangedInteractionTriggerStart(ControllerInteractionEventArgs e)
    {
        GameObject collidedObject = GetCollidedObject();
        if (collidedObject != null)
        {
            // If the object that is being pointed at is a chart, it is pullable
            if (collidedObject.tag == "Chart")
            {
                isPullable = true;
                rangedPullGameObject = collidedObject;
                rangedPullControllerStartPosition = transform.position;
                rangedPullObjectStartPosition = collidedObject.transform.position;
                SetInteractionState(InteractionState.RangedInteracting);
            }
        }
    }

    private void RangedInteractionLoop()
    {
        if (isPullable)
        {
            float distance = Vector3.Distance(rangedPullControllerStartPosition, transform.position);

            // Vibrate the controller based on how far away it is from the origin
            float vibrateAmount = 0.75f * (distance / rangedPullCompleteThreshold);
            VRTK_ControllerHaptics.TriggerHapticPulse(VRTK_ControllerReference.GetControllerReference(gameObject), vibrateAmount);

            if (distance > rangedPullStartThreshold)
            {
                isPullable = false;
                SetInteractionState(InteractionState.RangedPulling);
            }
        }
    }

    private void RangedInteractionTriggerEnd(ControllerInteractionEventArgs e)
    {
        if (previousState != InteractionState.None)
        {
            SetInteractionState(previousState);
            previousState = InteractionState.None;
        }
        else
        {
            SetInteractionState(InteractionState.RangedInteraction);
        }
        /*
        GameObject collidedObject = GetCollidedObject();
        // If the object the pointer is colliding with is the one which was initially "clicked"
        if (collidedObject != null && collidedObject == rangedPullGameObject)
        {
            if (collidedObject.tag == "HtmlElement")
            {
                collidedObject.GetComponent<InteractableHtmlElement>().Click();
            }
            else if (collidedObject.tag == "HtmlAxisElement")
            {
                collidedObject.GetComponent<InteractableHtmlAxisElement>().Click();
            }
        }
        */
        isPullable = false;
    }

    private void RangedPullLoop()
    {
        float distance = Vector3.Distance(rangedPullControllerStartPosition, transform.position);

        // Vibrate the controller based on how far away it is from the origin
        float vibrateAmount = 0.75f * (distance / rangedPullCompleteThreshold);
        VRTK_ControllerHaptics.TriggerHapticPulse(VRTK_ControllerReference.GetControllerReference(gameObject), vibrateAmount);

        // If the object has been pulled sufficiently far, grab it
        if (distance > rangedPullCompleteThreshold)
        {
            SetInteractionState(InteractionState.None);

            rangedPullGameObject.transform.position = transform.position;

            GetComponent<VRTK_InteractTouch>().ForceTouch(rangedPullGameObject);
            GetComponent<VRTK_InteractGrab>().AttemptGrab();
        }
        else
        {
            Rigidbody rb = rangedPullGameObject.GetComponent<Rigidbody>();
            rb.MovePosition(Vector3.Lerp(rangedPullObjectStartPosition, transform.position, distance / rangedPullCompleteThreshold));

            // Lock LookAt rotation to only rotate along the y axis
            //Vector3 targetPosition = new Vector3(Camera.main.transform.position.x, rangedPullGameObject.transform.position.y, Camera.main.transform.position.z);
            //rangedPullGameObject.transform.LookAt(targetPosition);
        }
    }

    private void RangedPullTriggerEnd(ControllerInteractionEventArgs e)
    {
        if (previousState != InteractionState.None)
        {
            SetInteractionState(previousState);
            previousState = InteractionState.None;
        }
        else
        {
            SetInteractionState(InteractionState.RangedInteraction);
        }

        //TODO: Fix
        //rangedPullCreatedGameObject.GetComponent<Chart>().AnimateTowards(rangedPullGameObject.transform.position,  0.1f);
    }

    // Override the color of the laser such that it is still invalid when hitting the just the screen itself
    protected override void CheckRayHit(bool rayHit, RaycastHit pointerCollidedWith)
    {
        base.CheckRayHit(rayHit, pointerCollidedWith);

        if (rayHit && (pointerCollidedWith.collider.gameObject == screen))
        {
            ChangeColor(invalidCollisionColor);
        }
    }

    private GameObject GetCollidedObject()
    {
        RaycastHit tmp;
        return GetCollidedObject(out tmp);
    }

    private GameObject GetCollidedObject(out RaycastHit pointerCollidedWith)
    {
        Transform origin = GetOrigin();
        Ray pointerRaycast = new Ray(origin.position, origin.forward);
        bool rayHit = VRTK_CustomRaycast.Raycast(customRaycast, pointerRaycast, out pointerCollidedWith, defaultIgnoreLayer, maximumLength);

        if (pointerCollidedWith.collider != null)
            return pointerCollidedWith.collider.gameObject;
        else
            return null;
    }

    // Checks if a point is within a specified polygon that is defined by an array of points
    public bool ContainsPoint(Vector2[] polygon, Vector2 point)
    {
        int polygonLength = polygon.Length, i = 0;
        bool inside = false;
        // x, y for tested point.
        float pointX = point.x, pointY = point.y;
        // start / end point for the current polygon segment.
        float startX, startY, endX, endY;
        Vector2 endPoint = polygon[polygonLength - 1];
        endX = endPoint.x;
        endY = endPoint.y;
        while (i < polygonLength)
        {
            startX = endX; startY = endY;
            endPoint = polygon[i++];
            endX = endPoint.x; endY = endPoint.y;
            //
            inside ^= (endY > pointY ^ startY > pointY) /* ? pointY inside [startY;endY] segment ? */
                      && /* if so, test if it is under the segment */
                      ((pointX - endX) < (pointY - endY) * (startX - endX) / (startY - endY));
        }
        return inside;
    }
}