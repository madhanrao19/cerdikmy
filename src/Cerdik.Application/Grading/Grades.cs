namespace Cerdik.Application.Grading;

/// <summary>Maps a 0–100 percentage to a Malaysian-style letter grade (A+ … G). Shared by mock
/// exams and predictive insights so the scale stays consistent.</summary>
public static class Grades
{
    public static string Letter(double percent) => percent switch
    {
        >= 90 => "A+",
        >= 80 => "A",
        >= 70 => "B+",
        >= 65 => "B",
        >= 60 => "C+",
        >= 50 => "C",
        >= 45 => "D",
        >= 40 => "E",
        _ => "G",
    };
}
