class PuzzleActionResult {
  final List<String> playedMoves;
  final int? currentMoveIndex;
  final int? wrongMoves;
  final bool usedHint;
  final bool isSolved;
  final bool isCompleted;
  final bool isExpired;
  final String? hintSquare;
  final int? score;

  const PuzzleActionResult({
    required this.playedMoves,
    required this.currentMoveIndex,
    required this.wrongMoves,
    required this.usedHint,
    required this.isSolved,
    required this.isCompleted,
    required this.isExpired,
    required this.hintSquare,
    required this.score,
  });

  factory PuzzleActionResult.fromJson(Map<String, dynamic> json) {
    dynamic read(String key) =>
        json[key] ?? json['${key[0].toUpperCase()}${key.substring(1)}'];
    int? asInt(dynamic value) =>
        value is num ? value.toInt() : int.tryParse(value?.toString() ?? '');

    final moves = read('playedMoves');
    return PuzzleActionResult(
      playedMoves: moves is List
          ? moves.map((move) => move.toString()).toList()
          : const [],
      currentMoveIndex: asInt(read('currentMoveIndex')),
      wrongMoves: asInt(read('wrongMoves')),
      usedHint: read('usedHint') == true,
      isSolved: read('isSolved') == true,
      isCompleted: read('isCompleted') == true,
      isExpired: read('isExpired') == true,
      hintSquare: read('hintSquare')?.toString(),
      score: asInt(read('score')) ?? asInt(read('totalScore')),
    );
  }
}
