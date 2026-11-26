class Puzzle {
  final int id;
  final String difficulty;
  final bool isSolved;
  final int? previousScore;

  const Puzzle({
    required this.id,
    required this.difficulty,
    required this.isSolved,
    this.previousScore,
  });

  factory Puzzle.fromJson(Map<String, dynamic> json) {
    final rawPreviousScore =
        json['previousScore'] ?? json['PreviousScore'];

    int? previousScore;

    if (rawPreviousScore is num) {
      previousScore = rawPreviousScore.toInt();
    } else if (rawPreviousScore != null) {
      previousScore = int.tryParse(
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