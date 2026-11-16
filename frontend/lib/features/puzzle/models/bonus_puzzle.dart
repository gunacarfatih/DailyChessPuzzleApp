
class BonusPuzzle {
  final bool isEligible;
  final bool isCompleted;
  final bool isExpired;
  final int? puzzleId;
  final String? fen;
  final List<String> moves;
  final DateTime? startedAtUtc;
  final DateTime? completedAtUtc;
  final int? remainingSeconds;
  final int? score;

  const BonusPuzzle({
    required this.isEligible,
    required this.isCompleted,
    required this.isExpired,
    this.puzzleId,
    this.fen,
    this.moves = const [],
    this.startedAtUtc,
    this.completedAtUtc,
    this.remainingSeconds,
    this.score,
  });

  factory BonusPuzzle.fromJson(Map<String, dynamic> json) {
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

    int? parseInt(dynamic value) {
      if (value is num) return value.toInt();
      if (value == null) return null;
      return int.tryParse(value.toString());
    }

    DateTime? parseDate(dynamic value) {
      if (value == null) return null;
      return DateTime.tryParse(value.toString());
    }

    return BonusPuzzle(
      isEligible: json['isEligible'] == true,
      isCompleted: json['isCompleted'] == true,
      isExpired: json['isExpired'] == true,
      puzzleId: parseInt(json['puzzleId']),
      fen: json['fen']?.toString(),
      moves: parsedMoves,
      startedAtUtc: parseDate(json['startedAtUtc']),
      completedAtUtc: parseDate(json['completedAtUtc']),
      remainingSeconds: parseInt(json['remainingSeconds']),
      score: parseInt(json['score']),
    );
  }
}
