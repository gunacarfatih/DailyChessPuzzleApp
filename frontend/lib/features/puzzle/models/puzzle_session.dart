class PuzzleSession {
  final int? sessionId;
  final int puzzleId;
  final List<String> playedMoves;
  final int currentMoveIndex;
  final int wrongMoves;
  final bool usedHint;
  final bool isSolved;
  final bool isCompleted;
  final DateTime? startedAtUtc;
  final String? hintSquare;
  final int? score;

  const PuzzleSession({
    required this.sessionId,
    required this.puzzleId,
    required this.playedMoves,
    required this.currentMoveIndex,
    required this.wrongMoves,
    required this.usedHint,
    required this.isSolved,
    required this.isCompleted,
    required this.startedAtUtc,
    required this.hintSquare,
    required this.score,
  });

  factory PuzzleSession.fromJson(Map<String, dynamic> json) {
    dynamic read(String key) =>
        json[key] ?? json['${key[0].toUpperCase()}${key.substring(1)}'];
    int asInt(String key, [int fallback = 0]) =>
        (read(key) as num?)?.toInt() ??
        int.tryParse('${read(key)}') ??
        fallback;

    final moves = read('playedMoves');
    final started = read('startedAtUtc');
    return PuzzleSession(
      sessionId: int.tryParse('${read('sessionId')}'),
      puzzleId: asInt('puzzleId'),
      playedMoves: moves is List
          ? moves.map((move) => move.toString()).toList()
          : const [],
      currentMoveIndex: asInt('currentMoveIndex'),
      wrongMoves: asInt('wrongMoves'),
      usedHint: read('usedHint') == true,
      isSolved: read('isSolved') == true,
      isCompleted: read('isCompleted') == true,
      startedAtUtc: started == null
          ? null
          : DateTime.tryParse('$started')?.toUtc(),
      hintSquare: read('hintSquare')?.toString(),
      score: int.tryParse('${read('score')}'),
    );
  }
}
