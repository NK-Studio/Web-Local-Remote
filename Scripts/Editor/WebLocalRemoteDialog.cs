using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WebLocalRemote
{
    // EditorUtility.DisplayDialog 대신 창 안에 띄우는 모달. 창 전체를 덮는 반투명 막 위에 카드를 올린다.
    // Enter는 확인, Esc는 취소(취소 버튼이 없으면 확인)로 닫는다. 스타일은 WebLocalRemoteWindow.uss의 .dialog-*.
    internal static class WebLocalRemoteDialog
    {
        internal enum Kind { Info, Success, Warning, Error }

        internal static void Show(VisualElement root, Kind kind, string title, string message,
            string ok = "OK", string cancel = null, Action<bool> onClose = null)
        {
            root.Q(className: "dialog-overlay")?.RemoveFromHierarchy();

            var overlay = new VisualElement { focusable = true };
            overlay.AddToClassList("dialog-overlay");

            var card = new VisualElement();
            card.AddToClassList("dialog-card");
            card.AddToClassList("dialog-" + kind.ToString().ToLowerInvariant());
            overlay.Add(card);

            var header = new VisualElement();
            header.AddToClassList("dialog-header");
            var icon = new Image
            {
                image = EditorGUIUtility.IconContent(kind switch
                {
                    Kind.Success => "TestPassed",
                    Kind.Warning => "console.warnicon",
                    Kind.Error => "console.erroricon",
                    _ => "console.infoicon",
                }).image
            };
            icon.AddToClassList("dialog-icon");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("dialog-title");
            header.Add(icon);
            header.Add(titleLabel);
            card.Add(header);

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("dialog-body");
            var messageLabel = new Label(message) { selection = { isSelectable = true } };
            messageLabel.AddToClassList("dialog-message");
            body.Add(messageLabel);
            card.Add(body);

            var buttons = new VisualElement();
            buttons.AddToClassList("dialog-buttons");
            card.Add(buttons);

            void Close(bool result)
            {
                overlay.RemoveFromHierarchy();
                onClose?.Invoke(result);
            }

            if (cancel != null)
            {
                var cancelBtn = new Button(() => Close(false)) { text = cancel };
                cancelBtn.AddToClassList("dialog-btn");
                buttons.Add(cancelBtn);
            }
            var okBtn = new Button(() => Close(true)) { text = ok };
            okBtn.AddToClassList("dialog-btn");
            okBtn.AddToClassList("dialog-btn-primary");
            buttons.Add(okBtn);

            // 막 위의 클릭이 아래 창으로 새지 않게 막고, 키보드로도 닫을 수 있게 한다.
            overlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    Close(true);
                else if (evt.keyCode == KeyCode.Escape)
                    Close(cancel == null);
                else
                    return;
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            root.Add(overlay);
            overlay.schedule.Execute(() => okBtn.Focus());
        }
    }
}
