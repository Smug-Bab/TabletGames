using UnityEngine;
using UnityEngine.UIElements;

public class UIToolkitDpad : MonoBehaviour
{
    private UIDocument uiDocument;
    
    [HideInInspector] public static float verticalInput = 0f;
    [HideInInspector] public static float horizontalInput = 0f;

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        // Register button callbacks for UI Toolkit
        SetupButton(root.Q<Button>("BtnUp"), 0f, 1f);
        SetupButton(root.Q<Button>("BtnDown"), 0f, -1f);
        SetupButton(root.Q<Button>("BtnLeft"), -1f, 0f);
        SetupButton(root.Q<Button>("BtnRight"), 1f, 0f);
    }

    void SetupButton(Button btn, float hVal, float vVal)
    {
        if (btn == null) return;

        btn.RegisterCallback<PointerDownEvent>(evt => {
            if (hVal != 0) horizontalInput = hVal;
            if (vVal != 0) verticalInput = vVal;
        });

        btn.RegisterCallback<PointerUpEvent>(evt => {
            if (hVal != 0 && horizontalInput == hVal) horizontalInput = 0f;
            if (vVal != 0 && verticalInput == vVal) verticalInput = 0f;
        });
    }
}