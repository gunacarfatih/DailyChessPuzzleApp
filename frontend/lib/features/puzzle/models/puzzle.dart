class Puzzle {
  final int id;
  final String difficulty;
  final bool isSolved;
  final double? previousScore;

  const Puzzle({
    required this.id,
    required this.difficulty,
    required this.isSolved,
    this.previousScore,
  });

  factory Puzzle.fromJson(Map<String, dynamic> json) {
    final rawPreviousScore =
        json['previousScore'] ?? json['PreviousScore'];

    double? previousScore;

    if (rawPreviousScore is num) {
      previousScore = rawPreviousScore.toDouble();
    } else if (rawPreviousScore != null) {
      previousScore = double.tryParse(
        rawPreviousScore.toString(),
      );
    }

    return Puzzle(
      id: ((json['id'] ?? json['Id']) as num).toInt(),
      difficulty:
      (json['difficulty'] ?? json['Difficulty'])?.toString() ??
          'Bilinmiyor',
      isSolved:
      (json['isSolved'] ?? json['IsSolved']) == true,
      previousScore: previousScore,
    );
  }
}