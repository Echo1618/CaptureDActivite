
using UnityEngine;

public static class DebugPanelLayout
{
    public enum PanelSide
    {
        Left,
        Right
    }

    private const float Margin = 10f;
    private const float Gap = 10f;

    public static Rect GetRect(
        PanelSide side,
        float preferredWidth)
    {
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        float safeWidth = Mathf.Max(
            1f,
            screenWidth - Margin * 2f
        );

        float safeHeight = Mathf.Max(
            1f,
            screenHeight - Margin * 2f
        );

        preferredWidth = Mathf.Max(100f, preferredWidth);

        bool sideBySide =
            screenWidth >=
            (430f + 390f + Margin * 2f + Gap);

        if (sideBySide)
        {
            float columnWidth =
                (screenWidth - Margin * 2f - Gap) / 2f;

            float width = Mathf.Min(
                preferredWidth,
                columnWidth
            );

            float x = side == PanelSide.Left
                ? Margin
                : screenWidth - Margin - width;

            return new Rect(
                x,
                Margin,
                width,
                safeHeight
            );
        }

        // Petit écran : deux panneaux empilés.
        float panelHeight =
            Mathf.Max(1f, (safeHeight - Gap) / 2f);

        float y = side == PanelSide.Left
            ? Margin
            : Margin + panelHeight + Gap;

        return new Rect(
            Margin,
            y,
            safeWidth,
            panelHeight
        );
    }
}
