using Cerdik.Application.Dtos;

namespace Cerdik.Web.Services;

/// <summary>
/// Scoped holder of the currently-selected student (for parents with multiple
/// children, and to keep the student context consistent across pages). Raises
/// <see cref="OnChange"/> when the selection changes.
/// </summary>
public sealed class CurrentStudentState
{
    public Guid? StudentId { get; private set; }
    public StudentSummaryDto? Student { get; private set; }

    /// <summary>Raised whenever the selected student changes.</summary>
    public event Action? OnChange;

    public void Select(StudentSummaryDto student)
    {
        if (StudentId == student.Id) return;
        StudentId = student.Id;
        Student = student;
        OnChange?.Invoke();
    }

    public void SelectById(Guid studentId, IReadOnlyList<StudentSummaryDto> known)
    {
        var match = known.FirstOrDefault(s => s.Id == studentId);
        if (match is not null) { Select(match); return; }
        if (StudentId == studentId) return;
        StudentId = studentId;
        Student = null;
        OnChange?.Invoke();
    }

    /// <summary>Ensures a student is selected, defaulting to the first available one.</summary>
    public void EnsureDefault(IReadOnlyList<StudentSummaryDto> students)
    {
        if (StudentId is not null && students.Any(s => s.Id == StudentId)) return;
        var first = students.FirstOrDefault();
        if (first is not null) Select(first);
    }

    public void Clear()
    {
        if (StudentId is null) return;
        StudentId = null;
        Student = null;
        OnChange?.Invoke();
    }
}
