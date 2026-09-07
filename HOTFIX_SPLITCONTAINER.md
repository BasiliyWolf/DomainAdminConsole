# Domain Admin Console 0.3.0 — SplitContainer hotfix

Исправлен сбой при запуске:

`System.InvalidOperationException: Параметр SplitterDistance должен быть в границах Panel1MinSize и Width - Panel2MinSize.`

Причина: WinForms проверяет `SplitterDistance` сразу при присвоении, когда `SplitContainer` ещё не получил реальный размер от `Dock`-layout.

В этой сборке все разделители создаются через `CreateSafeSplitContainer()`. Желаемая позиция разделителя и минимальные размеры панелей применяются только после появления достаточного фактического размера контейнера.
