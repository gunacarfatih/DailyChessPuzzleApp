
import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

class ClientIdStorage {
  static const String _clientIdKey = 'client_id';

  static const Uuid _uuid = Uuid();

  Future<String> getClientId() async {
    final preferences = await SharedPreferences.getInstance();

    String? clientId = preferences.getString(_clientIdKey);

    if (clientId == null || clientId.isEmpty) {
      clientId = _uuid.v4();

      await preferences.setString(
        _clientIdKey,
        clientId,
      );
    }

    return clientId;
  }
}
