
class BonusPuzzle {
  final bool isEligible;
  final bool isCompleted;
  final bool isExpired;
  final int? puzzleId;
  final String? fen;

  // Sunucunun onayladığı hamleler.
  final List<String> playedMoves;
  final int currentMoveIndex;
  final int wrongMoves;
  final bool usedHint;
  final String? hintSquare;

  final DateTime? startedAtUtc;
  final DateTime? completedAtUtc;
  final int? remainingSeconds;
  final double? score;

  const BonusPuzzle({
    required this.isEligible,
    required this.isCompleted,
    required this.isExpired,
    this.puzzleId,
    this.fen,
    this.playedMoves = const [],
    this.currentMoveIndex = 0,
    this.wrongMoves = 0,
    this.usedHint = false,
    this.hintSquare,
    this.startedAtUtc,
    this.completedAtUtc,
    this.remainingSeconds,
    this.score,
  });

  factory BonusPuzzle.fromJson(Map<String, dynamic> json) {
    int? parseInt(dynamic value) {
      if (value is num) return value.toInt();
      if (value == null) return null;
      return int.tryParse(value.toString());
    }

    DateTime? parseDate(dynamic value) {
      if (value == null) return null;
      return DateTime.tryParse(value.toString());
    }

    final rawPlayedMoves =
        json['playedMoves'] ?? json['PlayedMoves'];

    final parsedPlayedMoves = rawPlayedMoves is List
        ? rawPlayedMoves.map((move) => move.toString()).toList()
        : <String>[];

    return BonusPuzzle(
      isEligible:
      (json['isEligible'] ?? json['IsEligible']) == true,
      isCompleted:
      (json['isCompleted'] ?? json['IsCompleted']) == true,
      isExpired:
      (json['isExpired'] ?? json['IsExpired']) == true,
      puzzleId: parseInt(json['puzzleId'] ?? json['PuzzleId']),
      fen: (json['fen'] ?? json['Fen'])?.toString(),
      playedMoves: parsedPlayedMoves,
      currentMoveIndex: parseInt(
        json['currentMoveIndex'] ?? json['CurrentMoveIndex'],
      ) ??
          0,
      wrongMoves: parseInt(
        json['wrongMoves'] ?? json['WrongMoves'],
      ) ??
          0,
      usedHint:
      (json['usedHint'] ?? json['UsedHint']) == true,
      hintSquare:
      (json['hintSquare'] ?? json['HintSquare'])?.toString(),
      startedAtUtc: parseDate(
        json['startedAtUtc'] ?? json['StartedAtUtc'],
      ),
      completedAtUtc: parseDate(
        json['completedAtUtc'] ?? json['CompletedAtUtc'],
      ),
      remainingSeconds: parseInt(
        json['remainingSeconds'] ?? json['RemainingSeconds'],
      ),
      score: double.tryParse('${json['score'] ?? json['Score']}'),
    );
  }
}
