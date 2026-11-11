
class LeaderboardEntry {
  final int rank;
  final String clientId;
  final int totalScore;
  final int solvedCount;

  const LeaderboardEntry({
    required this.rank,
    required this.clientId,
    required this.totalScore,
    required this.solvedCount,
  });

  factory LeaderboardEntry.fromJson(Map<String, dynamic> json) {
    return LeaderboardEntry(
      rank: (json['rank'] as num?)?.toInt() ?? 0,
      clientId: json['clientId']?.toString() ?? '',
      totalScore: (json['totalScore'] as num?)?.toInt() ?? 0,
      solvedCount: (json['solvedCount'] as num?)?.toInt() ?? 0,
    );
  }
}
