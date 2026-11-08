import Foundation
import NaturalLanguage
import SwiftUI
import Translation
import UIKit

// Keep the bridge loadable on older systems by guarding Translation API calls.
@objc(OGTranslationBridge)
@MainActor
public final class TranslationBridge: NSObject {
    private var host: UIViewController?
    private var finish: ((String?, String?, String?, Int) -> Void)?
    private var requestID: UUID?
    private var translationStarted = false
    private var cancellationRequested = false

    @objc public static var isSupported: Bool {
        if #available(iOS 18.0, macCatalyst 26.0, *) { return true }
        return false
    }

    @objc(getLanguagesWithCompletion:)
    public func getLanguages(completion: @escaping ([String], Int) -> Void) {
        guard #available(iOS 18.0, macCatalyst 26.0, *) else {
            completion([], 1)
            return
        }
        Task { @MainActor in
            let languages = await LanguageAvailability().supportedLanguages
            completion(languages.map { $0.minimalIdentifier }, 0)
        }
    }

    @objc(translateText:source:target:presenter:completion:)
    public func translate(text: String, source: String?, target: String, presenter: UIViewController,
                          completion: @escaping (String?, String?, String?, Int) -> Void) {
        guard #available(iOS 18.0, macCatalyst 26.0, *) else {
            completion(nil, nil, nil, 1)
            return
        }
        // The managed scheduler waits for completion before starting another request.
        guard requestID == nil else {
            completion(nil, nil, nil, 5)
            return
        }
        let sourceCode = source ?? NLLanguageRecognizer.dominantLanguage(for: text)?.rawValue
        guard let sourceCode else {
            completion(nil, nil, nil, 3)
            return
        }
        let sourceLanguage = Locale.Language(identifier: sourceCode)
        let targetLanguage = Locale.Language(identifier: target)
        if sourceLanguage.minimalIdentifier == targetLanguage.minimalIdentifier {
            completion(text, sourceCode, target, 7)
            return
        }
        let id = UUID()
        requestID = id
        translationStarted = false
        cancellationRequested = false
        finish = completion
        let sourceFailureStatus = source == nil ? 3 : 2
        Task { @MainActor [weak self, weak presenter] in
            let availability = LanguageAvailability()
            let status = await availability.status(from: sourceLanguage, to: targetLanguage)
            guard let self, self.requestID == id else { return }
            guard status != .unsupported else {
                // The availability result alone does not distinguish a language from a pairing.
                let supported = (await availability.supportedLanguages).map(\.minimalIdentifier)
                let from = sourceLanguage.minimalIdentifier
                let to = targetLanguage.minimalIdentifier
                if supported.contains(from) && supported.contains(to) {
                    self.complete(id, nil, from, to, 11)
                } else {
                    self.complete(id, nil, nil, nil, sourceFailureStatus)
                }
                return
            }
            guard let presenter else {
                self.complete(id, nil, nil, nil, 1)
                return
            }
            let root = TranslationHost(text: text, source: sourceLanguage, target: targetLanguage,
                                       sourceFailureStatus: sourceFailureStatus, begin: { [weak self] in
                guard let self, self.requestID == id, !self.cancellationRequested, !self.translationStarted else { return false }
                self.translationStarted = true
                return true
            }) { [weak self] text, source, target, status in
                self?.complete(id, text, source, target, status)
            }
            let controller = UIHostingController(rootView: root)
            controller.view.backgroundColor = .clear
            controller.view.frame = CGRect(x: 0, y: 0, width: 1, height: 1)
            presenter.addChild(controller)
            presenter.view.addSubview(controller.view)
            controller.didMove(toParent: presenter)
            self.host = controller
        }
    }

    @objc public func cancel() {
        guard let id = requestID else { return }
        cancellationRequested = true
        removeHost()
        // Keep the managed scheduler occupied until the cancelled native task returns.
        if !translationStarted { complete(id, nil, nil, nil, 8) }
    }

    private func complete(_ id: UUID, _ text: String?, _ source: String?, _ target: String?, _ status: Int) {
        // A late native completion must not consume a newer request's callback or host.
        guard requestID == id else { return }
        requestID = nil
        let callback = finish
        finish = nil
        let wasCancelled = cancellationRequested
        removeHost()
        if wasCancelled { callback?(nil, nil, nil, 8) }
        else { callback?(text, source, target, status) }
    }

    private func removeHost() {
        let controller = host
        host = nil
        controller?.willMove(toParent: nil)
        controller?.view.removeFromSuperview()
        controller?.removeFromParent()
    }
}

@available(iOS 18.0, macCatalyst 26.0, *)
@MainActor
private struct TranslationHost: View {
    let text: String
    let source: Locale.Language
    let target: Locale.Language
    let sourceFailureStatus: Int
    let begin: () -> Bool
    let completion: (String?, String?, String?, Int) -> Void

    var body: some View {
        Color.clear
            .translationTask(source: source, target: target) { session in
                guard begin() else { return }
                var preparing = true
                do {
                    try Task.checkCancellation()
                    // The system owns model-download permission and progress UI.
                    try await session.prepareTranslation()
                    preparing = false
                    try Task.checkCancellation()
                    let response = try await session.translate(text)
                    try Task.checkCancellation()
                    completion(response.targetText, response.sourceLanguage.minimalIdentifier,
                               response.targetLanguage.minimalIdentifier, 0)
                } catch is CancellationError {
                    completion(nil, nil, nil, 8)
                } catch TranslationError.unableToIdentifyLanguage {
                    completion(nil, nil, nil, sourceFailureStatus)
                } catch TranslationError.unsupportedSourceLanguage {
                    completion(nil, source.minimalIdentifier, nil, 9)
                } catch TranslationError.unsupportedTargetLanguage {
                    completion(nil, nil, target.minimalIdentifier, 10)
                } catch TranslationError.unsupportedLanguagePairing {
                    completion(nil, source.minimalIdentifier, target.minimalIdentifier, 11)
                } catch TranslationError.nothingToTranslate {
                    completion(nil, nil, nil, 6)
                } catch {
                    // Never return native error descriptions, which may contain the message.
                    if #available(iOS 26.0, macCatalyst 26.0, *), case TranslationError.alreadyCancelled = error {
                        completion(nil, nil, nil, 8)
                    } else {
                        let native = error as NSError
                        let cancelled = Task.isCancelled
                            || (native.domain == NSCocoaErrorDomain && native.code == CocoaError.Code.userCancelled.rawValue)
                            || (native.domain == NSURLErrorDomain && native.code == URLError.Code.cancelled.rawValue)
                        completion(nil, nil, nil, cancelled ? 8 : preparing ? 4 : 5)
                    }
                }
            }
    }
}
