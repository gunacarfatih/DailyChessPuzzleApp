class Puzzle {
  final int id;
  final String fen;
  final List<String> moves;
  final String difficulty;
  final bool isSolved;
  final int? previousScore;

  const Puzzle({
    required this.id,
    required this.fen,
    required this.moves,
    required this.difficulty,
    required this.isSolved,
    this.previousScore,
  });

  factory Puzzle.fromJson(Map<String, dynamic> json) {
    final rawMoves = json['moves'];

    List<String> parsedMoves;

    if (rawMoves is String) {
      parsedMoves = rawMoves
          .split(RegExp(r'\s+'))
          .where((move) => move.isNotEmpty)
          .toList();
    } else if (rawMoves is List) {
      parsedMoves = rawMoves
          .map((move) => move.toString())
          .toList();
    } else {
      parsedMoves = [];
    }

    final rawPreviousScore = json['previousScore'] ??
        json['PreviousScore'];

    int? previousScore;

    if (rawPreviousScore is num) {
      previousScore = rawPreviousScore.toInt();
    } else if (rawPreviousScore != null) {
      previousScore = int.tryParse(
        rawPreviousScore.toString(),
      );
    }

    return Puzzle(
      id: (json['id'] as num).toInt(),
      fen: json['fen'] as String,
      moves: parsedMoves,
      difficulty: json['difficulty']?.toString() ?? 'Bilinmiyor',
      isSolved: json['isSolved'] == true,
      previousScore: previousScore,
    );
  }
}