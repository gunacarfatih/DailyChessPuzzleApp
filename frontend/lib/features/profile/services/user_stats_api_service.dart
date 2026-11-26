import 'package:dio/dio.dart';

import '../../../core/constants/api_constants.dart';
import '../../../core/models/api_response.dart';
import '../../../core/network/api_client.dart';
import '../../leaderboard/models/leaderboard_entry.dart';

class UserStatsApiService {
  final Dio _dio = ApiClient.dio;

  Future<ApiResponse<LeaderboardEntry>> getMyStats() async {
    final response = await _dio.get(ApiConstants.myStatsEndpoint);
    return ApiResponse<LeaderboardEntry>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => LeaderboardEntry.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );
  }
}
