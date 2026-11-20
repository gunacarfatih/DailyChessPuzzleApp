
class LeaderboardEntry {
  final int rank;
  final String publicId;
  final String username;
  final int totalScore;
  final int solvedCount;

  const LeaderboardEntry({
    required this.rank,
    required this.publicId,
    required this.username,
    required this.totalScore,
    required this.solvedCount,
  });

  factory LeaderboardEntry.fromJson(Map<String, dynamic> json) {
    return LeaderboardEntry(
      rank: (json['rank'] as num?)?.toInt() ?? 0,
      publicId: json['publicId']?.toString() ?? '',
      username: json['username']?.toString() ?? '',
      totalScore: (json['totalScore'] as num?)?.toInt() ?? 0,
      solvedCount: (json['solvedCount'] as num?)?.toInt() ?? 0,
    );
  }
}
