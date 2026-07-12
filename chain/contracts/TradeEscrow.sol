// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";

/// @title TradeProof Pay — conditional trade-settlement escrow
/// @notice Holds an ERC-20 amount per off-chain trade id and, after human/AI
///         approval performed off-chain, either releases the funds to the seller
///         or refunds the buyer. Invariants mirror the DemoMode mock:
///         a trade can be funded once, released once, and refunded once, and a
///         released trade can never be refunded (and vice versa).
/// @dev    Private keys are never held server-side. The buyer signs `fund`
///         with their own wallet. `release` / `refund` may be signed either by
///         the platform arbiter (`owner`) after off-chain approval, or by the
///         buyer directly. Settlement/refund finality must still be confirmed
///         server-side by checking the receipt status (spec Sec.4/Sec.17).
contract TradeEscrow is Ownable, ReentrancyGuard {
    using SafeERC20 for IERC20;

    enum Status {
        None,
        Funded,
        Released,
        Refunded
    }

    struct Escrow {
        address buyer;
        address seller;
        address token;
        uint256 amount;
        Status status;
    }

    /// @dev tradeId => escrow record
    mapping(uint256 => Escrow) private _escrows;

    /// @dev tradeId => (document/verdict/approval key => sha-256 hash) anchored on-chain (spec Sec.20)
    mapping(uint256 => mapping(bytes32 => bytes32)) public anchoredHash;

    event Funded(
        uint256 indexed tradeId,
        address indexed buyer,
        address indexed seller,
        address token,
        uint256 amount
    );
    event Released(uint256 indexed tradeId, address indexed seller, uint256 amount);
    event Refunded(uint256 indexed tradeId, address indexed buyer, uint256 amount);
    event HashAnchored(uint256 indexed tradeId, bytes32 indexed key, bytes32 value);

    constructor(address initialOwner) Ownable(initialOwner) {}

    /// @notice Fund the escrow for a trade (spec Sec.7). Caller (the buyer) must
    ///         have approved this contract to spend `amount` of `token` first.
    function fund(uint256 tradeId, address seller, address token, uint256 amount) external nonReentrant {
        require(seller != address(0), "seller required");
        require(token != address(0), "token required");
        require(amount > 0, "amount required");

        Escrow storage e = _escrows[tradeId];
        require(e.status == Status.None, "already funded");

        e.buyer = msg.sender;
        e.seller = seller;
        e.token = token;
        e.amount = amount;
        e.status = Status.Funded;

        IERC20(token).safeTransferFrom(msg.sender, address(this), amount);
        emit Funded(tradeId, msg.sender, seller, token, amount);
    }

    /// @notice Release the escrowed funds to the seller (spec Sec.16).
    ///         Rejects double settlement. Callable by owner (arbiter) or buyer.
    function release(uint256 tradeId) external nonReentrant {
        Escrow storage e = _escrows[tradeId];
        require(e.status == Status.Funded, "not funded");
        require(msg.sender == owner() || msg.sender == e.buyer, "not authorized");

        e.status = Status.Released;
        IERC20(e.token).safeTransfer(e.seller, e.amount);
        emit Released(tradeId, e.seller, e.amount);
    }

    /// @notice Refund the buyer after expiry (spec Sec.18).
    ///         Rejects double refund and refund-after-release. Callable by owner or buyer.
    function refund(uint256 tradeId) external nonReentrant {
        Escrow storage e = _escrows[tradeId];
        require(e.status == Status.Funded, "not funded");
        require(msg.sender == owner() || msg.sender == e.buyer, "not authorized");

        e.status = Status.Refunded;
        IERC20(e.token).safeTransfer(e.buyer, e.amount);
        emit Refunded(tradeId, e.buyer, e.amount);
    }

    /// @notice Anchor document / verdict / approval hashes on-chain (spec Sec.20).
    function anchorHashes(uint256 tradeId, bytes32[] calldata keys, bytes32[] calldata values) external onlyOwner {
        require(keys.length == values.length, "length mismatch");
        for (uint256 i = 0; i < keys.length; i++) {
            anchoredHash[tradeId][keys[i]] = values[i];
            emit HashAnchored(tradeId, keys[i], values[i]);
        }
    }

    /// @notice Read the escrow snapshot for a trade.
    function getEscrow(uint256 tradeId)
        external
        view
        returns (address buyer, address seller, address token, uint256 amount, Status status)
    {
        Escrow storage e = _escrows[tradeId];
        return (e.buyer, e.seller, e.token, e.amount, e.status);
    }
}
