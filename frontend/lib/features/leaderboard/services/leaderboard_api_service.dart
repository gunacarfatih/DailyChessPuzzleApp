
import 'package:dio/dio.dart';

import '../../../core/constants/api_constants.dart';
import '../../../core/models/api_response.dart';
import '../../../core/network/api_client.dart';
import '../models/leaderboard_response.dart';

class LeaderboardApiService {
  final Dio _dio = ApiClient.dio;

  Future<ApiResponse<LeaderboardResponse>> getLeaderboard({
    required String clientId,
    String? country,
    int limit = 10,
    DateTime? startDate,
    DateTime? endDate,
  }) async {
    final queryParameters = <String, dynamic>{
      'clientId': clientId,
      'limit': limit,
    };

    if (country != null && country.isNotEmpty) {
      queryParameters['country'] = country;
    }

    if (startDate != null) {
      queryParameters['startDate'] =
          _formatDate(startDate);
    }

    if (endDate != null) {
      queryParameters['endDate'] =
          _formatDate(endDate);
    }

    final response = await _dio.get(
      ApiConstants.leaderboardEndpoint,
      queryParameters: queryParameters,
    );

    return ApiResponse<LeaderboardResponse>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => LeaderboardResponse.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );
  }

  String _formatDate(DateTime date) {
    final year = date.year.toString().padLeft(4, '0');
    final month = date.month.toString().padLeft(2, '0');
    final day = date.day.toString().padLeft(2, '0');

    return '$year-$month-$day';
  }
}
