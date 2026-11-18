
import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../constants/api_constants.dart';
import '../storage/auth_storage.dart';

class LeaderboardSignalRService {
  HubConnection? _connection;

  bool get isConnected =>
      _connection?.state == HubConnectionState.Connected;

  // Liderlik tablosu güncellendiğinde çalışır.
  VoidCallback? onLeaderboardUpdated;

  // SignalR yeniden bağlandığında çalışır.
  VoidCallback? onConnectionRestored;


  Future<void> connect() async {
    debugPrint('SignalR: connect() çağrıldı.');

    if (isConnected) {
      debugPrint('SignalR: Zaten bağlı.');
      return;
    }

    try {
      final token = await AuthStorage.instance.getAccessToken();

      debugPrint(
        'SignalR: Token durumu: ${token == null || token.isEmpty ? "YOK" : "MEVCUT"}',
      );

      if (token == null || token.isEmpty) {
        debugPrint('SignalR: JWT token bulunamadı.');
        return;
      }

      final hubUrl = '${ApiConstants.baseUrl}/hubs/leaderboard';

      debugPrint('SignalR: Hub URL: $hubUrl');

      final httpOptions = HttpConnectionOptions(
        accessTokenFactory: () async => token,
      );

      _connection = HubConnectionBuilder()
          .withUrl(hubUrl, options: httpOptions)
          .withAutomaticReconnect()
          .build();

      _connection!.on('LeaderboardUpdated', (arguments) {
        debugPrint('SignalR: LeaderboardUpdated alındı: $arguments');
        onLeaderboardUpdated?.call();
      });

      _connection!.onclose(({error}) {
        debugPrint('SignalR bağlantısı kapandı: $error');
      });

      _connection!.onreconnected(({connectionId}) {
        debugPrint('SignalR yeniden bağlandı: $connectionId');
        onConnectionRestored?.call();
      });

      debugPrint('SignalR: Bağlantı başlatılıyor...');

      await _connection!.start();

      debugPrint('SignalR bağlantısı başarılı.');
    } catch (e, stackTrace) {
      debugPrint('SignalR bağlantı hatası: $e');
      debugPrint('SignalR stack trace: $stackTrace');
    }
  }


  Future<void> disconnect() async {
    final connection = _connection;

    if (connection == null) return;

    _connection = null;

    try {
      await connection.stop();
      debugPrint('SignalR bağlantısı kapatıldı.');
    } catch (e) {
      debugPrint('SignalR bağlantısı kapatılırken hata: $e');
    }
  }
}
